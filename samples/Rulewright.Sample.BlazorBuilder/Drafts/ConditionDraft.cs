using Rulewright.Core;
using Rulewright.Serialization;

namespace Rulewright.Sample.BlazorBuilder.Drafts;

/// <summary>
/// A mutable, UI-friendly counterpart to <see cref="ConditionNode"/> — Core's condition tree is
/// immutable and constructor-validated (a group can't be empty, a leaf's shape depends on its
/// operator), which is exactly wrong for an editor where the tree passes through transiently
/// invalid states while the user is mid-edit (an empty group right before they add its first
/// child, etc.). Editors bind to this tree; <see cref="ConditionDraftConverter"/> is the only
/// bridge back to real rule-schema JSON.
/// </summary>
public abstract class ConditionDraft
{
    /// <summary>Stable per-instance id for Blazor <c>@key</c> during reordering.</summary>
    public Guid Id { get; } = Guid.NewGuid();
}

/// <summary>An AND/OR/NOT combinator over child conditions.</summary>
public sealed class GroupDraft : ConditionDraft
{
    public LogicalOperator Operator { get; set; } = LogicalOperator.And;

    public List<ConditionDraft> Children { get; } = new();
}

/// <summary>
/// A field comparison. Phase C only supports a plain <c>field</c> left-hand side (not a computed
/// <c>expression</c> LHS — that needs the recursive value-expression editor, which lands with
/// the action editors); a leaf whose JSON used <c>expression</c> instead of <c>field</c> parses
/// as a <see cref="RawConditionDraft"/> instead of a <see cref="LeafDraft"/>.
/// </summary>
public sealed class LeafDraft : ConditionDraft
{
    public string Field { get; set; } = string.Empty;

    public ConditionOperator Operator { get; set; } = ConditionOperator.Equal;

    /// <summary>Registered function name; only meaningful when <see cref="Operator"/> is <c>custom</c>.</summary>
    public string? FunctionName { get; set; }

    /// <summary>
    /// The comparison operand, held as JSON text (e.g. <c>100</c>, <c>"gold"</c>,
    /// <c>["gold","vip"]</c>) rather than a typed CLR value — this is what value editors read
    /// from and write to, and what gets spliced verbatim into the rebuilt document. Empty for
    /// operators that take no value (<c>IsNull</c>/<c>IsNotNull</c>) or a valueless <c>custom</c> call.
    /// </summary>
    public string ValueJson { get; set; } = string.Empty;
}

/// <summary>
/// A condition node the builder doesn't (yet) understand how to edit visually — a computed
/// <c>expression</c> left-hand side, an unrecognized operator, or any other shape outside the
/// Phase C model. Round-trips its original JSON verbatim so loading a document the builder can't
/// fully parse into a tree never silently destroys data; the UI renders it read-only with a note
/// to edit it via the raw JSON view instead.
/// </summary>
public sealed class RawConditionDraft : ConditionDraft
{
    public RawConditionDraft(RuleJsonValue original) => Original = original;

    public RuleJsonValue Original { get; }
}
