using System;

namespace Rulewright.Execution;

/// <summary>
/// The compiled form of one rule for one fact type: an untraced fast-path predicate,
/// and a traced variant that records each condition node's outcome into a caller-supplied
/// slot array. Both are compiled once and cached; tracing therefore adds zero cost to
/// untraced evaluations.
/// </summary>
internal sealed class CompiledRule<TFact>
{
    internal CompiledRule(Func<TFact, bool> predicate, Func<TFact, bool?[], bool> tracedPredicate)
    {
        Predicate = predicate;
        TracedPredicate = tracedPredicate;
    }

    internal Func<TFact, bool> Predicate { get; }

    internal Func<TFact, bool?[], bool> TracedPredicate { get; }
}
