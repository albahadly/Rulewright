using Rulewright.Sample.BlazorBuilder.Json;
using Rulewright.Serialization;

namespace Rulewright.Sample.BlazorBuilder.Drafts;

public static class ValueExpressionDraftConverter
{
    public static ValueExpressionDraft FromJson(RuleJsonValue node)
    {
        if (node.Kind != RuleJsonValueKind.Object)
        {
            // A bare scalar (or null) is an implicit literal.
            return new LiteralExpressionDraft { ValueJson = RuleJsonValueWriter.ToJsonString(node, indented: false) };
        }

        if (node.TryGetProperty("literal", out RuleJsonValue literal))
        {
            return new LiteralExpressionDraft { ValueJson = RuleJsonValueWriter.ToJsonString(literal, indented: false) };
        }

        if (node.TryGetProperty("field", out RuleJsonValue field) && field.Kind == RuleJsonValueKind.String)
        {
            return new FieldExpressionDraft { Field = field.GetString() };
        }

        if (node.TryGetProperty("op", out RuleJsonValue opNode)
            && opNode.Kind == RuleJsonValueKind.String
            && RuleSchemaCatalog.TryGetExpressionOperator(opNode.GetString(), out ExpressionOperatorInfo opInfo)
            && node.TryGetProperty("operands", out RuleJsonValue operands)
            && operands.Kind == RuleJsonValueKind.Array)
        {
            var draft = new OperatorExpressionDraft { Operator = opInfo.Operator };
            draft.Operands.Clear();
            foreach (RuleJsonValue operand in operands.Items)
            {
                draft.Operands.Add(FromJson(operand));
            }

            return draft;
        }

        return new RawValueExpressionDraft(node);
    }

    public static RuleJsonValue ToJson(ValueExpressionDraft draft, IRuleJsonReader reader)
    {
        switch (draft)
        {
            case RawValueExpressionDraft raw:
                return raw.Original;

            case LiteralExpressionDraft literal:
                return LenientJsonParser.ParseOrQuote(literal.ValueJson, reader);

            case FieldExpressionDraft field:
                return RuleJsonValue.CreateObject(new[]
                {
                    new KeyValuePair<string, RuleJsonValue>("field", RuleJsonValue.CreateString(field.Field)),
                });

            case OperatorExpressionDraft op:
                string jsonName = RuleSchemaCatalog.ExpressionOperators.First(i => i.Operator == op.Operator).JsonName;
                return RuleJsonValue.CreateObject(new[]
                {
                    new KeyValuePair<string, RuleJsonValue>("op", RuleJsonValue.CreateString(jsonName)),
                    new KeyValuePair<string, RuleJsonValue>(
                        "operands", RuleJsonValue.CreateArray(op.Operands.Select(o => ToJson(o, reader)))),
                });

            default:
                throw new NotSupportedException($"Unknown value expression draft type '{draft.GetType()}'.");
        }
    }
}
