using System;

namespace Rulewright.Core;

/// <summary>
/// A leaf condition comparing a fact field (resolved via a dotted path such as
/// <c>Customer.Age</c>) against a constant value, or delegating to a registered
/// custom function.
/// </summary>
public sealed class ConditionLeaf : ConditionNode
{
    /// <summary>
    /// Creates a leaf condition.
    /// </summary>
    /// <param name="field">
    /// Dotted path resolved against the fact. Required for every operator except
    /// <see cref="ConditionOperator.Custom"/>, where it is optional (a custom function
    /// without a field receives the whole fact).
    /// </param>
    /// <param name="operator">The comparison operator.</param>
    /// <param name="value">
    /// The comparison operand: a CLR scalar (<see cref="string"/>, <see cref="bool"/>,
    /// <see cref="long"/>, <see cref="decimal"/>, <see cref="double"/>), null, or an
    /// object array for <see cref="ConditionOperator.In"/>/<see cref="ConditionOperator.NotIn"/>.
    /// </param>
    /// <param name="functionName">
    /// Registered function name; required when <paramref name="operator"/> is
    /// <see cref="ConditionOperator.Custom"/>.
    /// </param>
    /// <exception cref="ArgumentException">
    /// A custom leaf has no function name, or a non-custom leaf has no field.
    /// </exception>
    public ConditionLeaf(string? field, ConditionOperator @operator, object? value, string? functionName = null)
    {
        if (@operator == ConditionOperator.Custom)
        {
            if (string.IsNullOrEmpty(functionName))
            {
                throw new ArgumentException("A custom condition requires a function name.", nameof(functionName));
            }
        }
        else if (string.IsNullOrEmpty(field))
        {
            throw new ArgumentException("A non-custom condition requires a field path.", nameof(field));
        }

        Field = field;
        Operator = @operator;
        Value = value;
        FunctionName = functionName;
    }

    /// <summary>Dotted field path, or null for a field-less custom condition.</summary>
    public string? Field { get; }

    /// <summary>The comparison operator.</summary>
    public ConditionOperator Operator { get; }

    /// <summary>The constant comparison operand (null for IsNull/IsNotNull and field-only custom calls).</summary>
    public object? Value { get; }

    /// <summary>The registered custom function name when <see cref="Operator"/> is <see cref="ConditionOperator.Custom"/>.</summary>
    public string? FunctionName { get; }
}
