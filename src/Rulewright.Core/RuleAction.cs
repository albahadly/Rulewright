using System;

namespace Rulewright.Core;

/// <summary>
/// An action applied when a rule's condition passes. v1 supports a single action
/// type, <c>setOutput</c>: write a constant value into the evaluation result's outputs.
/// </summary>
public sealed class RuleAction
{
    /// <summary>The v1 <c>setOutput</c> action type identifier.</summary>
    public const string SetOutputType = "setOutput";

    /// <summary>
    /// Creates a rule action.
    /// </summary>
    /// <param name="type">The action type identifier; <c>setOutput</c> in v1.</param>
    /// <param name="target">The output key the action writes.</param>
    /// <param name="value">The constant value written to <paramref name="target"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="type"/> or <paramref name="target"/> is null or empty.</exception>
    public RuleAction(string type, string target, object? value)
    {
        if (string.IsNullOrEmpty(type))
        {
            throw new ArgumentException("Action type must not be null or empty.", nameof(type));
        }

        if (string.IsNullOrEmpty(target))
        {
            throw new ArgumentException("Action target must not be null or empty.", nameof(target));
        }

        Type = type;
        Target = target;
        Value = value;
    }

    /// <summary>The action type identifier (<c>setOutput</c> in v1).</summary>
    public string Type { get; }

    /// <summary>The output key the action writes.</summary>
    public string Target { get; }

    /// <summary>The constant value written to <see cref="Target"/>.</summary>
    public object? Value { get; }
}
