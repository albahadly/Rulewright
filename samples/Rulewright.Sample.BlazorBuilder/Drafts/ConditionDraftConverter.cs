using Rulewright.Core;
using Rulewright.Sample.BlazorBuilder.Json;
using Rulewright.Serialization;

namespace Rulewright.Sample.BlazorBuilder.Drafts;

/// <summary>
/// The bridge between a <see cref="ConditionDraft"/> tree and rule-schema JSON
/// (<see cref="RuleJsonValue"/>). Anything this converter doesn't recognize on the way in
/// round-trips as a <see cref="RawConditionDraft"/> rather than being dropped or guessed at.
/// </summary>
public static class ConditionDraftConverter
{
    public static ConditionDraft FromJson(RuleJsonValue node)
    {
        if (node.Kind != RuleJsonValueKind.Object)
        {
            return new RawConditionDraft(node);
        }

        if (node.TryGetProperty("type", out RuleJsonValue type))
        {
            return type.Kind == RuleJsonValueKind.String && type.GetString() == "group"
                ? FromGroupJson(node)
                : new RawConditionDraft(node);
        }

        return FromLeafJson(node);
    }

    private static ConditionDraft FromGroupJson(RuleJsonValue node)
    {
        if (!node.TryGetProperty("operator", out RuleJsonValue opNode) || opNode.Kind != RuleJsonValueKind.String)
        {
            return new RawConditionDraft(node);
        }

        LogicalOperator? logicalOperator = opNode.GetString() switch
        {
            "AND" => LogicalOperator.And,
            "OR" => LogicalOperator.Or,
            "NOT" => LogicalOperator.Not,
            _ => null,
        };

        if (logicalOperator is null
            || !node.TryGetProperty("rules", out RuleJsonValue rules)
            || rules.Kind != RuleJsonValueKind.Array)
        {
            return new RawConditionDraft(node);
        }

        var group = new GroupDraft { Operator = logicalOperator.Value };
        foreach (RuleJsonValue child in rules.Items)
        {
            group.Children.Add(FromJson(child));
        }

        return group;
    }

    private static ConditionDraft FromLeafJson(RuleJsonValue node)
    {
        // Phase C only edits field-based leaves; a computed expression LHS round-trips raw.
        if (node.TryGetProperty("expression", out _))
        {
            return new RawConditionDraft(node);
        }

        if (!node.TryGetProperty("operator", out RuleJsonValue opNode)
            || opNode.Kind != RuleJsonValueKind.String
            || !RuleSchemaCatalog.TryGetConditionOperator(opNode.GetString(), out ConditionOperatorInfo opInfo))
        {
            return new RawConditionDraft(node);
        }

        var leaf = new LeafDraft { Operator = opInfo.Operator };

        if (node.TryGetProperty("field", out RuleJsonValue field) && field.Kind == RuleJsonValueKind.String)
        {
            leaf.Field = field.GetString();
        }

        if (node.TryGetProperty("name", out RuleJsonValue name) && name.Kind == RuleJsonValueKind.String)
        {
            leaf.FunctionName = name.GetString();
        }

        if (node.TryGetProperty("value", out RuleJsonValue value))
        {
            leaf.ValueJson = RuleJsonValueWriter.ToJsonString(value, indented: false);
        }

        return leaf;
    }

    public static RuleJsonValue ToJson(ConditionDraft draft, IRuleJsonReader reader)
    {
        switch (draft)
        {
            case RawConditionDraft raw:
                return raw.Original;

            case GroupDraft group:
                return ToGroupJson(group, reader);

            case LeafDraft leaf:
                return ToLeafJson(leaf, reader);

            default:
                throw new NotSupportedException($"Unknown condition draft type '{draft.GetType()}'.");
        }
    }

    private static RuleJsonValue ToGroupJson(GroupDraft group, IRuleJsonReader reader)
    {
        string opName = group.Operator switch
        {
            LogicalOperator.And => "AND",
            LogicalOperator.Or => "OR",
            LogicalOperator.Not => "NOT",
            _ => "AND",
        };

        var items = group.Children.Select(child => ToJson(child, reader));

        return RuleJsonValue.CreateObject(new[]
        {
            new KeyValuePair<string, RuleJsonValue>("type", RuleJsonValue.CreateString("group")),
            new KeyValuePair<string, RuleJsonValue>("operator", RuleJsonValue.CreateString(opName)),
            new KeyValuePair<string, RuleJsonValue>("rules", RuleJsonValue.CreateArray(items)),
        });
    }

    private static RuleJsonValue ToLeafJson(LeafDraft leaf, IRuleJsonReader reader)
    {
        string jsonName = RuleSchemaCatalog.ConditionOperators.First(info => info.Operator == leaf.Operator).JsonName;

        var props = new List<KeyValuePair<string, RuleJsonValue>>();
        if (!string.IsNullOrEmpty(leaf.Field))
        {
            props.Add(new("field", RuleJsonValue.CreateString(leaf.Field)));
        }

        props.Add(new("operator", RuleJsonValue.CreateString(jsonName)));

        if (leaf.Operator == ConditionOperator.Custom && !string.IsNullOrEmpty(leaf.FunctionName))
        {
            props.Add(new("name", RuleJsonValue.CreateString(leaf.FunctionName)));
        }

        if (!string.IsNullOrWhiteSpace(leaf.ValueJson))
        {
            props.Add(new("value", LenientJsonParser.ParseOrQuote(leaf.ValueJson, reader)));
        }

        return RuleJsonValue.CreateObject(props);
    }
}
