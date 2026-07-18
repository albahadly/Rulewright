using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Rulewright.Core;
using Rulewright.Serialization;

namespace Rulewright.Execution;

/// <summary>
/// The Rulewright evaluation engine: loads rule sets (parse → validate → prepare),
/// compiles rules to delegates per fact type via expression trees, caches the compiled
/// delegates by rule content hash, and evaluates facts against them. Thread-safe: one
/// engine instance can serve concurrent evaluations.
/// </summary>
public sealed class RulewrightEngine
{
    private static readonly IReadOnlyDictionary<string, object?> EmptyOutputs =
        new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(StringComparer.Ordinal));

    private readonly IRuleJsonReader? _jsonReader;
    private readonly IReadOnlyDictionary<string, IRuleFunction> _functions;
    private readonly ConcurrentDictionary<CompiledCacheKey, object> _compiledRules =
        new ConcurrentDictionary<CompiledCacheKey, object>();

    internal RulewrightEngine(IRuleJsonReader? jsonReader, IReadOnlyDictionary<string, IRuleFunction> functions)
    {
        _jsonReader = jsonReader;
        _functions = functions;
    }

    /// <summary>
    /// Parses, validates, and prepares a JSON rule document (a single rule or a rule set)
    /// for evaluation. Parsing and validation happen exactly once here; per-fact-type
    /// compilation happens lazily on first evaluation and is cached by rule content hash.
    /// </summary>
    /// <param name="json">The rule document JSON.</param>
    /// <exception cref="ArgumentNullException"><paramref name="json"/> is null.</exception>
    /// <exception cref="InvalidOperationException">No JSON reader was configured on the builder.</exception>
    /// <exception cref="RuleParseException">The text is not well-formed JSON.</exception>
    /// <exception cref="RuleValidationException">The document fails schema validation.</exception>
    /// <exception cref="RuleCompilationException">A referenced custom function is not registered, or an action type is unknown.</exception>
    public LoadedRuleSet LoadRuleSet(string json)
    {
        if (json is null)
        {
            throw new ArgumentNullException(nameof(json));
        }

        RuleJsonValue document = RequireReader().Read(json);
        return LoadRuleSet(RuleSetParser.Parse(document));
    }

    /// <summary>
    /// Prepares an already-constructed domain <see cref="RuleSet"/> for evaluation,
    /// bypassing JSON entirely.
    /// </summary>
    /// <param name="ruleSet">The rule set.</param>
    /// <exception cref="ArgumentNullException"><paramref name="ruleSet"/> is null.</exception>
    /// <exception cref="RuleCompilationException">A referenced custom function is not registered, or an action type is unknown.</exception>
    public LoadedRuleSet LoadRuleSet(RuleSet ruleSet)
    {
        if (ruleSet is null)
        {
            throw new ArgumentNullException(nameof(ruleSet));
        }

        IEnumerable<Rule> ordered = ruleSet.Rules.OrderByDescending(rule => rule.Priority);
        var entries = new List<RuleEntry>(ruleSet.Rules.Count);
        foreach (Rule rule in ordered)
        {
            ValidateFunctions(rule, rule.Condition);

            var outputs = new Dictionary<string, object?>(StringComparer.Ordinal);
            bool hasComplexOutputs = false;
            foreach (RuleAction action in rule.Actions)
            {
                if (!string.Equals(action.Type, RuleAction.SetOutputType, StringComparison.Ordinal)
                    && !string.Equals(action.Type, RuleAction.AddToOutputType, StringComparison.Ordinal)
                    && !string.Equals(action.Type, RuleAction.AppendToOutputType, StringComparison.Ordinal))
                {
                    throw new RuleCompilationException(rule.Id, $"unknown action type '{action.Type}'.");
                }

                if (OutputApplier.IsLiteralSet(action))
                {
                    outputs[action.Target] = ((LiteralExpression)action.Value).Value;
                }
                else
                {
                    // Computed or accumulating actions apply to the running result per
                    // evaluation rather than being pre-materialized here.
                    hasComplexOutputs = true;
                }
            }

            Dictionary<ConditionNode, int> nodeIndex = ConditionNodeIndexer.BuildIndexMap(rule.Condition, out int nodeCount);
            entries.Add(new RuleEntry(
                rule,
                RuleHasher.ComputeHash(rule),
                hasComplexOutputs ? EmptyOutputs : new ReadOnlyDictionary<string, object?>(outputs),
                hasComplexOutputs,
                nodeIndex,
                nodeCount));
        }

        return new LoadedRuleSet(ruleSet, entries.ToArray());
    }

    /// <summary>
    /// Validates JSON against the Rulewright schema contract without loading it,
    /// returning structured errors with JSON pointer paths. Malformed JSON is reported
    /// as a single root-level error rather than an exception, making this directly
    /// bindable from editor/UI validation.
    /// </summary>
    /// <param name="json">The rule document JSON.</param>
    /// <exception cref="ArgumentNullException"><paramref name="json"/> is null.</exception>
    /// <exception cref="InvalidOperationException">No JSON reader was configured on the builder.</exception>
    public RuleSetValidationResult Validate(string json)
    {
        if (json is null)
        {
            throw new ArgumentNullException(nameof(json));
        }

        RuleJsonValue document;
        try
        {
            document = RequireReader().Read(json);
        }
        catch (RuleParseException ex)
        {
            return new RuleSetValidationResult(new[] { new RuleValidationError(string.Empty, ex.Message) });
        }

        return RuleSetValidator.Validate(document);
    }

    /// <summary>
    /// Evaluates a fact against a loaded rule set. Typed facts run compiled delegates
    /// (cached per fact type and rule content hash); dictionary facts
    /// (<c>IDictionary&lt;string, object&gt;</c>) run the interpreter, reported via
    /// <see cref="RuleEvaluationResult.CompilationMode"/>.
    /// </summary>
    /// <typeparam name="TFact">The fact type.</typeparam>
    /// <param name="ruleSet">A rule set from <see cref="LoadRuleSet(string)"/>.</param>
    /// <param name="fact">The fact instance.</param>
    /// <param name="options">Per-evaluation options; defaults to <see cref="EvaluationOptions.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="ruleSet"/> or <paramref name="fact"/> is null.</exception>
    /// <exception cref="RuleCompilationException">A rule cannot be compiled against <typeparamref name="TFact"/> (missing field path, incompatible value type).</exception>
    public RuleEvaluationResult Evaluate<TFact>(LoadedRuleSet ruleSet, TFact fact, EvaluationOptions? options = null)
    {
        if (ruleSet is null)
        {
            throw new ArgumentNullException(nameof(ruleSet));
        }

        if (fact is null)
        {
            throw new ArgumentNullException(nameof(fact));
        }

        options ??= EvaluationOptions.Default;

        if (fact is System.Collections.IDictionary or IDictionary<string, object?>)
        {
            object boxedFact = fact;
            return EvaluateCore(
                ruleSet,
                options,
                CompilationMode.Interpreted,
                (entry, results) => RuleInterpreter.Evaluate(
                    entry.Rule.Condition, boxedFact, _functions, results, entry.NodeIndex),
                (entry, running) => ApplyInterpretedOutputs(entry, boxedFact, running));
        }

        return EvaluateCore(
            ruleSet,
            options,
            CompilationMode.Compiled,
            (entry, results) =>
            {
                CompiledRule<TFact> compiled = GetOrCompile<TFact>(entry);
                return results is null ? compiled.Predicate(fact) : compiled.TracedPredicate(fact, results);
            },
            (entry, running) => ApplyCompiledOutputs<TFact>(entry, fact, running));
    }

    /// <summary>
    /// Applies a fired rule's outputs to the running result and returns the rule's own view
    /// of what it wrote. Rules made purely of constant <c>setOutput</c> actions overwrite
    /// their shared, pre-materialized outputs; other rules evaluate each action's value and
    /// apply it (set/add/append) in order via <see cref="OutputApplier"/>.
    /// </summary>
    private static IReadOnlyDictionary<string, object?> ApplyInterpretedOutputs(
        RuleEntry entry, object fact, IDictionary<string, object?> running)
    {
        if (!entry.HasComplexOutputs)
        {
            foreach (KeyValuePair<string, object?> output in entry.Outputs)
            {
                running[output.Key] = output.Value;
            }

            return entry.Outputs;
        }

        var snapshot = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (RuleAction action in entry.Rule.Actions)
        {
            object? value = ActionExpressionInterpreter.EvaluateValue(action.Value, fact);
            OutputApplier.Apply(running, snapshot, action.Type, action.Target, value);
        }

        return new ReadOnlyDictionary<string, object?>(snapshot);
    }

    private IReadOnlyDictionary<string, object?> ApplyCompiledOutputs<TFact>(
        RuleEntry entry, TFact fact, IDictionary<string, object?> running)
    {
        if (!entry.HasComplexOutputs)
        {
            foreach (KeyValuePair<string, object?> output in entry.Outputs)
            {
                running[output.Key] = output.Value;
            }

            return entry.Outputs;
        }

        OutputStep<TFact>[] steps = GetOrCompile<TFact>(entry).OutputSteps!;
        var snapshot = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (OutputStep<TFact> step in steps)
        {
            object? value = step.ValueFactory(fact);
            OutputApplier.Apply(running, snapshot, step.Type, step.Target, value);
        }

        return new ReadOnlyDictionary<string, object?>(snapshot);
    }

    private RuleEvaluationResult EvaluateCore(
        LoadedRuleSet loaded,
        EvaluationOptions options,
        CompilationMode mode,
        Func<RuleEntry, bool?[]?, bool> evaluateRule,
        Func<RuleEntry, IDictionary<string, object?>, IReadOnlyDictionary<string, object?>> applyOutputs)
    {
        List<RuleTrace>? traceRules = options.EnableTrace
            ? new List<RuleTrace>(loaded.OrderedRules.Length)
            : null;
        var fired = new List<FiredRule>();
        var outputs = new Dictionary<string, object?>(StringComparer.Ordinal);
        bool stopped = false;

        foreach (RuleEntry entry in loaded.OrderedRules)
        {
            if (stopped || !entry.Rule.Enabled)
            {
                traceRules?.Add(new RuleTrace(entry.Rule.Id, fired: false, skipped: true, condition: null));
                continue;
            }

            bool matched;
            ConditionTraceNode? conditionTrace = null;
            if (traceRules is not null)
            {
                var results = new bool?[entry.NodeCount];
                matched = evaluateRule(entry, results);
                conditionTrace = ConditionTraceBuilder.Build(entry.Rule.Condition, entry.NodeIndex, results);
            }
            else
            {
                matched = evaluateRule(entry, null);
            }

            traceRules?.Add(new RuleTrace(entry.Rule.Id, matched, skipped: false, conditionTrace));

            if (matched)
            {
                // applyOutputs writes into the running outputs (overwrite, add, or append)
                // and returns this rule's own view of what it wrote.
                IReadOnlyDictionary<string, object?> ruleOutputs = applyOutputs(entry, outputs);
                fired.Add(new FiredRule(entry.Rule.Id, ruleOutputs));

                if (options.StopOnFirstMatch)
                {
                    stopped = true;
                }
            }
        }

        return new RuleEvaluationResult(
            fired,
            new ReadOnlyDictionary<string, object?>(outputs),
            mode,
            traceRules is null ? null : new EvaluationTrace(traceRules));
    }

    private CompiledRule<TFact> GetOrCompile<TFact>(RuleEntry entry)
    {
        var key = new CompiledCacheKey(typeof(TFact), entry.Hash);
        if (_compiledRules.TryGetValue(key, out object? cached))
        {
            return (CompiledRule<TFact>)cached;
        }

        return (CompiledRule<TFact>)_compiledRules.GetOrAdd(
            key,
            _ => RuleExpressionCompiler.Compile<TFact>(entry.Rule, _functions, entry.NodeIndex));
    }

    private void ValidateFunctions(Rule rule, ConditionNode node)
    {
        switch (node)
        {
            case ConditionGroup group:
                foreach (ConditionNode child in group.Children)
                {
                    ValidateFunctions(rule, child);
                }

                break;

            case ConditionLeaf { Operator: ConditionOperator.Custom } leaf
                when !_functions.ContainsKey(leaf.FunctionName!):
                throw new RuleCompilationException(
                    rule.Id,
                    $"custom function '{leaf.FunctionName}' is not registered. "
                    + "Register it with RulewrightBuilder.RegisterFunction before loading the rule set.");
        }
    }

    private IRuleJsonReader RequireReader()
        => _jsonReader ?? throw new InvalidOperationException(
            "No JSON reader is configured. Call RulewrightBuilder.UseJsonReader(...) with an adapter "
            + "such as SystemTextJsonReader (Rulewright.Json.SystemText) before loading JSON.");

    private readonly struct CompiledCacheKey : IEquatable<CompiledCacheKey>
    {
        private readonly Type _factType;
        private readonly string _ruleHash;

        internal CompiledCacheKey(Type factType, string ruleHash)
        {
            _factType = factType;
            _ruleHash = ruleHash;
        }

        public bool Equals(CompiledCacheKey other)
            => ReferenceEquals(_factType, other._factType)
            && string.Equals(_ruleHash, other._ruleHash, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is CompiledCacheKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (_factType.GetHashCode() * 397) ^ StringComparer.Ordinal.GetHashCode(_ruleHash);
            }
        }
    }
}
