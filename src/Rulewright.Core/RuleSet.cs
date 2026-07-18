using System;
using System.Collections.Generic;
using System.Linq;

namespace Rulewright.Core;

/// <summary>
/// An ordered collection of rules parsed from a single JSON document.
/// Immutable after construction.
/// </summary>
public sealed class RuleSet
{
    /// <summary>
    /// Creates a rule set.
    /// </summary>
    /// <param name="rules">The rules, in document order.</param>
    /// <param name="name">Optional display name.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rules"/> is null or contains null.</exception>
    /// <exception cref="ArgumentException"><paramref name="rules"/> is empty or contains duplicate rule ids.</exception>
    public RuleSet(IEnumerable<Rule> rules, string? name = null)
    {
        if (rules is null)
        {
            throw new ArgumentNullException(nameof(rules));
        }

        Rule[] materialized = rules.ToArray();
        if (materialized.Length == 0)
        {
            throw new ArgumentException("A rule set must contain at least one rule.", nameof(rules));
        }

        if (materialized.Any(r => r is null))
        {
            throw new ArgumentNullException(nameof(rules), "Rules must not contain null.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Rule rule in materialized)
        {
            if (!seen.Add(rule.Id))
            {
                throw new ArgumentException($"Duplicate rule id '{rule.Id}'.", nameof(rules));
            }
        }

        Rules = materialized;
        Name = name;
    }

    /// <summary>Optional display name.</summary>
    public string? Name { get; }

    /// <summary>The rules, in document order.</summary>
    public IReadOnlyList<Rule> Rules { get; }
}
