using Rulewright.Sample.BlazorBuilder.Drafts;
using Rulewright.Sample.BlazorBuilder.Json;
using Rulewright.Serialization;

namespace Rulewright.Sample.BlazorBuilder.Services;

/// <summary>
/// The rule document currently being edited, plus its live validation result and (for a
/// single-rule document) visually editable draft trees for its <see cref="Condition"/>,
/// <see cref="Actions"/>, and <see cref="ElseActions"/>. The raw JSON text in <see cref="Json"/>
/// is always the source of truth; the drafts are derived from it on every successful parse and,
/// when edited, are converted back to JSON and re-parsed — there is no separate persisted "draft
/// state" that can drift from the text. Scoped per browser tab/session (see DI registration in
/// Program.cs).
/// </summary>
public sealed class RuleDocumentState
{
    private readonly EngineService _engine;
    private RuleJsonValue? _parsedDocument;

    public RuleDocumentState(EngineService engine)
    {
        _engine = engine;
        Json = DefaultDocument;
        Revalidate();
    }

    public const string DefaultDocument = """
        {
          "id": "starter-rule",
          "condition": { "field": "Order.Total", "operator": "GreaterThan", "value": 100 },
          "actions": [ { "type": "setOutput", "target": "Discount", "value": 10 } ]
        }
        """;

    /// <summary>The document's current raw JSON text.</summary>
    public string Json { get; private set; } = DefaultDocument;

    /// <summary>The result of the last validation pass (structural errors with JSON pointers).</summary>
    public RuleSetValidationResult Validation { get; private set; } = RuleSetValidationResult.Success;

    /// <summary>The parse error message, if <see cref="Json"/> isn't even well-formed JSON.</summary>
    public string? ParseError { get; private set; }

    /// <summary>
    /// Whether the document is a single rule (has a top-level <c>condition</c>, not a
    /// <c>rules</c> set or a <c>decisionTable</c>) — the only shape the visual editors support
    /// today. A <c>rules</c>/<c>decisionTable</c> document is still fully usable via the raw JSON
    /// view.
    /// </summary>
    public bool SupportsConditionEditor { get; private set; }

    /// <summary>The document's condition tree as an editable draft, or null when <see cref="SupportsConditionEditor"/> is false.</summary>
    public ConditionDraft? Condition { get; private set; }

    /// <summary>
    /// The document's <c>actions</c> as editable drafts, or null when <see cref="SupportsConditionEditor"/>
    /// is false or <c>actions</c> is present but not an array (malformed).
    /// </summary>
    public List<ActionDraft>? Actions { get; private set; }

    /// <summary>The document's <c>else</c> actions as editable drafts. Empty (not null) when the rule has no <c>else</c> branch.</summary>
    public List<ActionDraft>? ElseActions { get; private set; }

    /// <summary>Raised whenever <see cref="Json"/> or <see cref="Validation"/> changes.</summary>
    public event Action? Changed;

    /// <summary>Replaces the document text (e.g. from the raw JSON editor or the example picker) and revalidates.</summary>
    public void SetJson(string json)
    {
        Json = json;
        Revalidate();
        Changed?.Invoke();
    }

    /// <summary>
    /// Applies an edited condition tree: converts it to JSON, splices it into the current
    /// document in place of the existing <c>condition</c> property, and re-serializes — the same
    /// path as if the user had hand-edited the raw JSON. No-ops if the current document isn't
    /// parseable (shouldn't happen while <see cref="SupportsConditionEditor"/> is true).
    /// </summary>
    public void ReplaceCondition(ConditionDraft condition)
    {
        if (_parsedDocument is null)
        {
            return;
        }

        RuleJsonValue newConditionJson = ConditionDraftConverter.ToJson(condition, _engine.JsonReader);
        RuleJsonValue newDocument = ReplaceProperty(_parsedDocument, "condition", newConditionJson);
        SetJson(RuleJsonValueWriter.ToJsonString(newDocument));
    }

    /// <summary>
    /// Applies the currently held <see cref="Actions"/>/<see cref="ElseActions"/> drafts back
    /// into the document — the same splice-and-reserialize path as <see cref="ReplaceCondition"/>.
    /// An empty <see cref="ElseActions"/> omits the <c>else</c> key entirely (rather than writing
    /// an empty array), matching "no else branch".
    /// </summary>
    public void CommitActions()
    {
        if (_parsedDocument is null || Actions is null)
        {
            return;
        }

        RuleJsonValue newDocument = ReplaceProperty(
            _parsedDocument,
            "actions",
            RuleJsonValue.CreateArray(Actions.Select(a => ActionDraftConverter.ToJson(a, _engine.JsonReader))));

        newDocument = ElseActions is { Count: > 0 }
            ? ReplaceProperty(
                newDocument,
                "else",
                RuleJsonValue.CreateArray(ElseActions.Select(a => ActionDraftConverter.ToJson(a, _engine.JsonReader))))
            : RemoveProperty(newDocument, "else");

        SetJson(RuleJsonValueWriter.ToJsonString(newDocument));
    }

    private void Revalidate()
    {
        ParseError = null;
        Condition = null;
        Actions = null;
        ElseActions = null;
        SupportsConditionEditor = false;
        _parsedDocument = null;

        try
        {
            RuleJsonValue document = _engine.JsonReader.Read(Json);
            Validation = RuleSetValidator.Validate(document);

            if (document.Kind == RuleJsonValueKind.Object
                && !document.TryGetProperty("rules", out _)
                && !document.TryGetProperty("decisionTable", out _)
                && document.TryGetProperty("condition", out RuleJsonValue conditionNode))
            {
                _parsedDocument = document;
                SupportsConditionEditor = true;
                Condition = ConditionDraftConverter.FromJson(conditionNode);
                Actions = ReadActionArray(document, "actions");
                ElseActions = ReadActionArray(document, "else");
            }
        }
        catch (RuleParseException ex)
        {
            ParseError = ex.Message;
            Validation = RuleSetValidationResult.Success;
        }
    }

    private static List<ActionDraft>? ReadActionArray(RuleJsonValue document, string property)
    {
        if (!document.TryGetProperty(property, out RuleJsonValue node))
        {
            return new List<ActionDraft>();
        }

        return node.Kind == RuleJsonValueKind.Array
            ? node.Items.Select(ActionDraftConverter.FromJson).ToList()
            : null;
    }

    private static RuleJsonValue ReplaceProperty(RuleJsonValue document, string name, RuleJsonValue value)
    {
        var properties = new List<KeyValuePair<string, RuleJsonValue>>();
        bool replaced = false;
        foreach (KeyValuePair<string, RuleJsonValue> property in document.Properties)
        {
            if (property.Key == name)
            {
                properties.Add(new KeyValuePair<string, RuleJsonValue>(name, value));
                replaced = true;
            }
            else
            {
                properties.Add(property);
            }
        }

        if (!replaced)
        {
            properties.Add(new KeyValuePair<string, RuleJsonValue>(name, value));
        }

        return RuleJsonValue.CreateObject(properties);
    }

    private static RuleJsonValue RemoveProperty(RuleJsonValue document, string name)
        => RuleJsonValue.CreateObject(document.Properties.Where(p => p.Key != name));
}
