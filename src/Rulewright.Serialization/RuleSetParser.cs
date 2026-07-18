using System;
using System.Collections.Generic;
using Rulewright.Core;

namespace Rulewright.Serialization;

/// <summary>
/// Maps a validated JSON document to the immutable <see cref="RuleSet"/> domain model.
/// The <c>layout</c> key is presentation metadata and is skipped entirely.
/// </summary>
public static class RuleSetParser
{
    /// <summary>
    /// Validates and parses a rule document (a single rule or a rule set).
    /// </summary>
    /// <param name="document">The document root.</param>
    /// <returns>The parsed rule set; a single-rule document becomes a one-rule set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is null.</exception>
    /// <exception cref="RuleValidationException">The document fails structural validation.</exception>
    public static RuleSet Parse(RuleJsonValue document)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        RuleSetValidationResult validation = RuleSetValidator.Validate(document);
        if (!validation.IsValid)
        {
            throw new RuleValidationException(validation.Errors);
        }

        if (document.TryGetProperty("rules", out RuleJsonValue rules))
        {
            string? name = document.TryGetProperty("name", out RuleJsonValue nameValue)
                ? nameValue.GetString()
                : null;

            var parsed = new List<Rule>(rules.Items.Count);
            foreach (RuleJsonValue rule in rules.Items)
            {
                parsed.Add(ParseRule(rule));
            }

            return new RuleSet(parsed, name);
        }

        return new RuleSet(new[] { ParseRule(document) });
    }

    private static Rule ParseRule(RuleJsonValue rule)
    {
        rule.TryGetProperty("id", out RuleJsonValue id);
        rule.TryGetProperty("condition", out RuleJsonValue condition);

        string? description = rule.TryGetProperty("description", out RuleJsonValue descriptionValue)
            ? descriptionValue.GetString()
            : null;

        int priority = 0;
        if (rule.TryGetProperty("priority", out RuleJsonValue priorityValue)
            && priorityValue.TryGetInt64(out long parsedPriority))
        {
            priority = (int)parsedPriority;
        }

        bool enabled = !rule.TryGetProperty("enabled", out RuleJsonValue enabledValue)
            || enabledValue.Kind == RuleJsonValueKind.True;

        List<RuleAction>? actions = null;
        if (rule.TryGetProperty("actions", out RuleJsonValue actionsValue))
        {
            actions = new List<RuleAction>(actionsValue.Items.Count);
            foreach (RuleJsonValue action in actionsValue.Items)
            {
                action.TryGetProperty("type", out RuleJsonValue type);
                action.TryGetProperty("target", out RuleJsonValue target);
                action.TryGetProperty("value", out RuleJsonValue value);
                actions.Add(new RuleAction(type.GetString(), target.GetString(), ParseValueExpression(value)));
            }
        }

        return new Rule(
            id.GetString(),
            ParseCondition(condition),
            actions,
            description,
            priority,
            enabled);
    }

    private static ValueExpression ParseValueExpression(RuleJsonValue node)
    {
        if (node.Kind == RuleJsonValueKind.Object)
        {
            if (node.TryGetProperty("op", out RuleJsonValue op))
            {
                ExpressionOperatorMap.TryParse(op.GetString(), out ExpressionOperator @operator);
                node.TryGetProperty("operands", out RuleJsonValue operands);
                var parsedOperands = new List<ValueExpression>(operands.Items.Count);
                foreach (RuleJsonValue operand in operands.Items)
                {
                    parsedOperands.Add(ParseValueExpression(operand));
                }

                return new OperatorExpression(@operator, parsedOperands);
            }

            if (node.TryGetProperty("field", out RuleJsonValue field))
            {
                return new FieldExpression(field.GetString());
            }

            // Validation guarantees an explicit-literal object here; { "literal": <scalar> }.
            node.TryGetProperty("literal", out RuleJsonValue literal);
            return new LiteralExpression(literal.ToClrValue());
        }

        // A bare scalar is a literal.
        return new LiteralExpression(node.ToClrValue());
    }

    private static ConditionNode ParseCondition(RuleJsonValue condition)
    {
        if (condition.TryGetProperty("type", out _))
        {
            condition.TryGetProperty("operator", out RuleJsonValue groupOperator);
            LogicalOperator logical = groupOperator.GetString() switch
            {
                "AND" => LogicalOperator.And,
                "OR" => LogicalOperator.Or,
                _ => LogicalOperator.Not,
            };

            condition.TryGetProperty("rules", out RuleJsonValue children);
            var parsedChildren = new List<ConditionNode>(children.Items.Count);
            foreach (RuleJsonValue child in children.Items)
            {
                parsedChildren.Add(ParseCondition(child));
            }

            return new ConditionGroup(logical, parsedChildren);
        }

        condition.TryGetProperty("operator", out RuleJsonValue leafOperator);
        OperatorMap.TryParse(leafOperator.GetString(), out ConditionOperator @operator);

        string? field = condition.TryGetProperty("field", out RuleJsonValue fieldValue)
            ? fieldValue.GetString()
            : null;

        string? functionName = condition.TryGetProperty("name", out RuleJsonValue nameValue)
            ? nameValue.GetString()
            : null;

        object? value = null;
        if (condition.TryGetProperty("value", out RuleJsonValue operand))
        {
            if (@operator is ConditionOperator.In or ConditionOperator.NotIn)
            {
                var items = new object?[operand.Items.Count];
                for (int i = 0; i < operand.Items.Count; i++)
                {
                    items[i] = operand.Items[i].ToClrValue();
                }

                value = items;
            }
            else
            {
                value = operand.ToClrValue();
            }
        }

        return new ConditionLeaf(field, @operator, value, functionName);
    }
}
