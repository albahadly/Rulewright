using System;
using System.Collections.Generic;
using System.Globalization;
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

        if (document.TryGetProperty("decisionTable", out RuleJsonValue decisionTable))
        {
            return ExpandDecisionTable(decisionTable);
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

    /// <summary>
    /// Expands a decision table into ordinary rules — one rule per row — so the engine
    /// evaluates it through the normal compiled/interpreted path with no special casing.
    /// Each input column contributes a condition leaf (a null cell is a wildcard); a row with
    /// all-wildcard inputs is a catch-all. The <c>first</c> hit policy is baked into the
    /// conditions by ANDing each row with the negation of every earlier row's own condition,
    /// so exactly the first matching row fires under normal evaluation.
    /// </summary>
    private static RuleSet ExpandDecisionTable(RuleJsonValue table)
    {
        string? name = table.TryGetProperty("name", out RuleJsonValue nameValue) && nameValue.Kind == RuleJsonValueKind.String
            ? nameValue.GetString()
            : null;
        string idBase = table.TryGetProperty("id", out RuleJsonValue idValue) && idValue.Kind == RuleJsonValueKind.String
            ? idValue.GetString()
            : "row";
        bool firstPolicy = table.TryGetProperty("hitPolicy", out RuleJsonValue hitPolicy)
            && hitPolicy.Kind == RuleJsonValueKind.String
            && hitPolicy.GetString() == "first";

        table.TryGetProperty("inputs", out RuleJsonValue inputs);
        table.TryGetProperty("outputs", out RuleJsonValue outputs);
        table.TryGetProperty("rows", out RuleJsonValue rows);

        int inputCount = inputs.Items.Count;
        var fields = new string[inputCount];
        var operators = new ConditionOperator[inputCount];
        for (int c = 0; c < inputCount; c++)
        {
            inputs.Items[c].TryGetProperty("field", out RuleJsonValue field);
            fields[c] = field.GetString();
            operators[c] = ConditionOperator.Equal;
            if (inputs.Items[c].TryGetProperty("operator", out RuleJsonValue op))
            {
                OperatorMap.TryParse(op.GetString(), out operators[c]);
            }
        }

        int outputCount = outputs.Items.Count;
        var targets = new string[outputCount];
        var types = new string[outputCount];
        for (int c = 0; c < outputCount; c++)
        {
            outputs.Items[c].TryGetProperty("target", out RuleJsonValue target);
            targets[c] = target.GetString();
            types[c] = outputs.Items[c].TryGetProperty("type", out RuleJsonValue type) && type.Kind == RuleJsonValueKind.String
                ? type.GetString()
                : RuleAction.SetOutputType;
        }

        int rowCount = rows.Items.Count;
        var ownConditions = new ConditionNode[rowCount];
        var expanded = new List<Rule>(rowCount);
        for (int r = 0; r < rowCount; r++)
        {
            rows.Items[r].TryGetProperty("when", out RuleJsonValue when);
            var leaves = new List<ConditionNode>();
            for (int c = 0; c < inputCount; c++)
            {
                RuleJsonValue cell = when.Items[c];
                if (cell.Kind == RuleJsonValueKind.Null)
                {
                    continue; // wildcard: no condition for this column
                }

                object? comparand = operators[c] is ConditionOperator.In or ConditionOperator.NotIn
                    ? ParseArrayCell(cell)
                    : cell.ToClrValue();
                leaves.Add(new ConditionLeaf(fields[c], operators[c], comparand));
            }

            ConditionNode ownCondition = leaves.Count switch
            {
                0 => AlwaysTrue(fields[0]),
                1 => leaves[0],
                _ => new ConditionGroup(LogicalOperator.And, leaves),
            };
            ownConditions[r] = ownCondition;

            ConditionNode condition = ownCondition;
            if (firstPolicy && r > 0)
            {
                var parts = new List<ConditionNode>(r + 1) { ownCondition };
                for (int p = 0; p < r; p++)
                {
                    parts.Add(new ConditionGroup(LogicalOperator.Not, new[] { ownConditions[p] }));
                }

                condition = new ConditionGroup(LogicalOperator.And, parts);
            }

            rows.Items[r].TryGetProperty("then", out RuleJsonValue then);
            var actions = new List<RuleAction>();
            for (int c = 0; c < outputCount; c++)
            {
                RuleJsonValue cell = then.Items[c];
                if (cell.Kind == RuleJsonValueKind.Null)
                {
                    continue; // this row does not write this output
                }

                actions.Add(new RuleAction(types[c], targets[c], ParseValueExpression(cell)));
            }

            string id = idBase + "-" + r.ToString(CultureInfo.InvariantCulture);
            expanded.Add(new Rule(id, condition, actions, description: null, priority: rowCount - r));
        }

        return new RuleSet(expanded, name);
    }

    private static object?[] ParseArrayCell(RuleJsonValue cell)
    {
        var items = new object?[cell.Items.Count];
        for (int i = 0; i < cell.Items.Count; i++)
        {
            items[i] = cell.Items[i].ToClrValue();
        }

        return items;
    }

    private static ConditionNode AlwaysTrue(string field)
        => new ConditionGroup(
            LogicalOperator.Or,
            new ConditionNode[]
            {
                new ConditionLeaf(field, ConditionOperator.IsNotNull, null),
                new ConditionLeaf(field, ConditionOperator.IsNull, null),
            });

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

        if (condition.TryGetProperty("expression", out RuleJsonValue leftExpression))
        {
            return new ConditionLeaf(ParseValueExpression(leftExpression), @operator, value);
        }

        return new ConditionLeaf(field, @operator, value, functionName);
    }
}
