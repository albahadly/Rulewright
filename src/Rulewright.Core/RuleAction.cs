using System;

namespace Rulewright.Core;

/// <summary>
/// An action applied when a rule's condition passes. Every action writes a
/// <see cref="ValueExpression"/> into the evaluation result's outputs under
/// <see cref="Target"/>: a constant is simply a <see cref="LiteralExpression"/>, and a value
/// derived from the fact is a <see cref="FieldExpression"/> or <see cref="OperatorExpression"/>.
/// The <see cref="Type"/> decides how the value combines with what fired rules have already
/// written: <c>setOutput</c> replaces, <c>addToOutput</c> accumulates numerically, and
/// <c>appendToOutput</c> collects into a list.
/// </summary>
public sealed class RuleAction
{
    /// <summary>Writes the value to <see cref="Target"/>, replacing any existing value.</summary>
    public const string SetOutputType = "setOutput";

    /// <summary>
    /// Numerically adds the value to <see cref="Target"/> (a running total across fired
    /// rules); a null value contributes nothing.
    /// </summary>
    public const string AddToOutputType = "addToOutput";

    /// <summary>
    /// Appends the value to a list at <see cref="Target"/> (collected across fired rules);
    /// a null value contributes nothing.
    /// </summary>
    public const string AppendToOutputType = "appendToOutput";

    /// <summary>
    /// Creates a rule action from a value expression.
    /// </summary>
    /// <param name="type">The action type identifier; <c>setOutput</c>.</param>
    /// <param name="target">The output key the action writes.</param>
    /// <param name="value">The value expression evaluated to produce the output.</param>
    /// <exception cref="ArgumentException"><paramref name="type"/> or <paramref name="target"/> is null or empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public RuleAction(string type, string target, ValueExpression value)
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
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Creates a rule action that writes a constant scalar, a convenience for the common
    /// case (equivalent to passing <c>new LiteralExpression(constant)</c>).
    /// </summary>
    /// <param name="type">The action type identifier; <c>setOutput</c>.</param>
    /// <param name="target">The output key the action writes.</param>
    /// <param name="constant">The constant value written to <paramref name="target"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="type"/> or <paramref name="target"/> is null or empty.</exception>
    public RuleAction(string type, string target, object? constant)
        : this(type, target, new LiteralExpression(constant))
    {
    }

    /// <summary>The action type identifier (<c>setOutput</c>).</summary>
    public string Type { get; }

    /// <summary>The output key the action writes.</summary>
    public string Target { get; }

    /// <summary>
    /// The value expression evaluated against the fact to produce the output. A constant
    /// action carries a <see cref="LiteralExpression"/>.
    /// </summary>
    public ValueExpression Value { get; }
}
