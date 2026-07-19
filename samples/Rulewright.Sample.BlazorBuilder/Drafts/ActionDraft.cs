using Rulewright.Core;

namespace Rulewright.Sample.BlazorBuilder.Drafts;

/// <summary>A mutable, UI-friendly counterpart to <see cref="RuleAction"/>.</summary>
public sealed class ActionDraft
{
    /// <summary>Stable per-instance id for Blazor <c>@key</c> during reordering.</summary>
    public Guid Id { get; } = Guid.NewGuid();

    public string Type { get; set; } = RuleAction.SetOutputType;

    public string Target { get; set; } = string.Empty;

    /// <summary>The value expression; null for <c>removeOutput</c>, which carries no value.</summary>
    public ValueExpressionDraft? Value { get; set; } = new LiteralExpressionDraft();
}
