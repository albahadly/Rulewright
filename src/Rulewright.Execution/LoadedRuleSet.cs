using System;
using System.Collections.Generic;
using Rulewright.Core;

namespace Rulewright.Execution;

/// <summary>
/// A rule set that has been parsed, validated, and prepared for execution by a
/// <see cref="RulewrightEngine"/>: rules are pre-sorted by priority, per-rule content
/// hashes are computed for the compiled-delegate cache, action outputs are
/// prematerialized, and condition nodes are pre-indexed for tracing. Compilation to
/// delegates happens lazily per fact type on first evaluation and is then cached.
/// </summary>
public sealed class LoadedRuleSet
{
    internal LoadedRuleSet(RuleSet ruleSet, RuleEntry[] orderedRules)
    {
        RuleSet = ruleSet;
        OrderedRules = orderedRules;
    }

    /// <summary>The underlying immutable rule set.</summary>
    public RuleSet RuleSet { get; }

    internal RuleEntry[] OrderedRules { get; }
}

/// <summary>
/// Per-rule execution metadata prepared once at load time.
/// </summary>
internal sealed class RuleEntry
{
    internal RuleEntry(
        Rule rule,
        string hash,
        IReadOnlyDictionary<string, object?> outputs,
        Dictionary<ConditionNode, int> nodeIndex,
        int nodeCount)
    {
        Rule = rule;
        Hash = hash;
        Outputs = outputs;
        NodeIndex = nodeIndex;
        NodeCount = nodeCount;
    }

    internal Rule Rule { get; }

    /// <summary>Content hash of the rule's condition and actions — the compiled-delegate cache key.</summary>
    internal string Hash { get; }

    /// <summary>The outputs this rule's actions produce when it fires (constant in v1, so shared).</summary>
    internal IReadOnlyDictionary<string, object?> Outputs { get; }

    /// <summary>Pre-order index of every condition node, shared by tracing across execution modes.</summary>
    internal Dictionary<ConditionNode, int> NodeIndex { get; }

    internal int NodeCount { get; }
}
