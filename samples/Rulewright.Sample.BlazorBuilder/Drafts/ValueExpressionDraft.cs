using Rulewright.Core;
using Rulewright.Serialization;

namespace Rulewright.Sample.BlazorBuilder.Drafts;

/// <summary>
/// A mutable, UI-friendly counterpart to <see cref="ValueExpression"/> — the computed-value AST
/// shared by action <c>value</c>s and a condition leaf's computed <c>expression</c> left-hand
/// side. See <see cref="ConditionDraft"/> for why this needs a separate mutable model from Core's
/// immutable one.
/// </summary>
public abstract class ValueExpressionDraft
{
    /// <summary>Stable per-instance id for Blazor <c>@key</c> during reordering.</summary>
    public Guid Id { get; } = Guid.NewGuid();
}

/// <summary>A constant. Held as JSON text (e.g. <c>10</c>, <c>"gold"</c>, <c>null</c>), not a typed CLR value.</summary>
public sealed class LiteralExpressionDraft : ValueExpressionDraft
{
    public string ValueJson { get; set; } = "null";
}

/// <summary>A fact field reference.</summary>
public sealed class FieldExpressionDraft : ValueExpressionDraft
{
    public string Field { get; set; } = string.Empty;
}

/// <summary>An operator applied to operand sub-expressions.</summary>
public sealed class OperatorExpressionDraft : ValueExpressionDraft
{
    public ExpressionOperator Operator { get; set; } = ExpressionOperator.Add;

    public List<ValueExpressionDraft> Operands { get; } = new()
    {
        new LiteralExpressionDraft(),
        new LiteralExpressionDraft(),
    };
}

/// <summary>An expression node shape the builder doesn't understand; round-trips verbatim. See <see cref="RawConditionDraft"/>.</summary>
public sealed class RawValueExpressionDraft : ValueExpressionDraft
{
    public RawValueExpressionDraft(RuleJsonValue original) => Original = original;

    public RuleJsonValue Original { get; }
}
