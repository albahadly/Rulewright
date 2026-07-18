using System;

namespace Rulewright.Core;

/// <summary>
/// The trace of a single rule within an evaluation: whether it fired, whether it
/// was skipped, and the per-node condition results.
/// </summary>
public sealed class RuleTrace
{
    /// <summary>
    /// Creates a rule trace entry.
    /// </summary>
    /// <param name="ruleId">The rule's id.</param>
    /// <param name="fired">Whether the rule's condition passed.</param>
    /// <param name="skipped">
    /// Whether the rule was never evaluated (disabled, or short-circuited by
    /// <see cref="EvaluationOptions.StopOnFirstMatch"/>).
    /// </param>
    /// <param name="condition">The condition trace tree, or null when the rule was skipped.</param>
    /// <exception cref="ArgumentException"><paramref name="ruleId"/> is null or empty.</exception>
    public RuleTrace(string ruleId, bool fired, bool skipped, ConditionTraceNode? condition)
    {
        if (string.IsNullOrEmpty(ruleId))
        {
            throw new ArgumentException("Rule id must not be null or empty.", nameof(ruleId));
        }

        RuleId = ruleId;
        Fired = fired;
        Skipped = skipped;
        Condition = condition;
    }

    /// <summary>The rule's id.</summary>
    public string RuleId { get; }

    /// <summary>Whether the rule's condition passed.</summary>
    public bool Fired { get; }

    /// <summary>Whether the rule was never evaluated (disabled, or short-circuited by stop-on-first-match).</summary>
    public bool Skipped { get; }

    /// <summary>The condition trace tree, or null when the rule was skipped.</summary>
    public ConditionTraceNode? Condition { get; }
}
