using System;
using System.Collections.Generic;

namespace Rulewright.Execution;

/// <summary>
/// The compiled form of one rule for one fact type: an untraced fast-path predicate,
/// a traced variant that records each condition node's outcome into a caller-supplied
/// slot array, and — for rules with at least one computed action — a delegate that
/// produces the rule's outputs from the fact. All are compiled once and cached; tracing
/// therefore adds zero cost to untraced evaluations, and rules whose outputs are all
/// constant carry no output delegate at all.
/// </summary>
internal sealed class CompiledRule<TFact>
{
    internal CompiledRule(
        Func<TFact, bool> predicate,
        Func<TFact, bool?[], bool> tracedPredicate,
        Func<TFact, IReadOnlyDictionary<string, object?>>? produceOutputs)
    {
        Predicate = predicate;
        TracedPredicate = tracedPredicate;
        ProduceOutputs = produceOutputs;
    }

    internal Func<TFact, bool> Predicate { get; }

    internal Func<TFact, bool?[], bool> TracedPredicate { get; }

    /// <summary>
    /// Produces this rule's outputs from the fact, or null when every action is a constant
    /// <c>setOutput</c> (the engine then reuses the rule's pre-materialized outputs).
    /// </summary>
    internal Func<TFact, IReadOnlyDictionary<string, object?>>? ProduceOutputs { get; }
}
