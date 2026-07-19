using Rulewright.Core;
using Rulewright.Serialization;

namespace Rulewright.Sample.BlazorBuilder.Drafts;

public static class ActionDraftConverter
{
    public static ActionDraft FromJson(RuleJsonValue node)
    {
        var draft = new ActionDraft { Value = null };

        if (node.Kind != RuleJsonValueKind.Object)
        {
            return draft;
        }

        if (node.TryGetProperty("type", out RuleJsonValue type) && type.Kind == RuleJsonValueKind.String)
        {
            draft.Type = type.GetString();
        }

        if (node.TryGetProperty("target", out RuleJsonValue target) && target.Kind == RuleJsonValueKind.String)
        {
            draft.Target = target.GetString();
        }

        if (draft.Type != RuleAction.RemoveOutputType && node.TryGetProperty("value", out RuleJsonValue value))
        {
            draft.Value = ValueExpressionDraftConverter.FromJson(value);
        }

        return draft;
    }

    public static RuleJsonValue ToJson(ActionDraft draft, IRuleJsonReader reader)
    {
        var props = new List<KeyValuePair<string, RuleJsonValue>>
        {
            new("type", RuleJsonValue.CreateString(draft.Type)),
            new("target", RuleJsonValue.CreateString(draft.Target)),
        };

        if (draft.Type != RuleAction.RemoveOutputType && draft.Value is not null)
        {
            props.Add(new("value", ValueExpressionDraftConverter.ToJson(draft.Value, reader)));
        }

        return RuleJsonValue.CreateObject(props);
    }
}
