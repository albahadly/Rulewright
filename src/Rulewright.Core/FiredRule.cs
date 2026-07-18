using System;
using System.Collections.Generic;

namespace Rulewright.Core;

/// <summary>
/// A rule whose condition passed during an evaluation, with the outputs its
/// actions produced.
/// </summary>
public sealed class FiredRule
{
    /// <summary>
    /// Creates a fired-rule record.
    /// </summary>
    /// <param name="ruleId">The id of the rule that fired.</param>
    /// <param name="outputs">The outputs produced by the rule's actions (target → value).</param>
    /// <exception cref="ArgumentException"><paramref name="ruleId"/> is null or empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="outputs"/> is null.</exception>
    public FiredRule(string ruleId, IReadOnlyDictionary<string, object?> outputs)
    {
        if (string.IsNullOrEmpty(ruleId))
        {
            throw new ArgumentException("Rule id must not be null or empty.", nameof(ruleId));
        }

        RuleId = ruleId;
        Outputs = outputs ?? throw new ArgumentNullException(nameof(outputs));
    }

    /// <summary>The id of the rule that fired.</summary>
    public string RuleId { get; }

    /// <summary>The outputs produced by the rule's actions (target → value).</summary>
    public IReadOnlyDictionary<string, object?> Outputs { get; }
}
