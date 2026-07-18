using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Rulewright.Serialization;

/// <summary>
/// Structurally validates a rule document against the Rulewright schema contract
/// (<c>docs/schema/rule-schema.json</c>) before parsing/compilation, producing
/// errors with JSON pointer paths. Usable standalone — this is the surface a
/// rule-builder UI's real-time validation hooks into.
/// </summary>
public static class RuleSetValidator
{
    /// <summary>
    /// Validates a parsed JSON document as a rule or rule set.
    /// </summary>
    /// <param name="document">The document root.</param>
    /// <returns>A result carrying zero or more pointer-addressed errors.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is null.</exception>
    public static RuleSetValidationResult Validate(RuleJsonValue document)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        var errors = new List<RuleValidationError>();
        if (document.Kind != RuleJsonValueKind.Object)
        {
            errors.Add(new RuleValidationError(string.Empty, "The document root must be a JSON object."));
            return new RuleSetValidationResult(errors);
        }

        if (document.TryGetProperty("rules", out _))
        {
            ValidateRuleSet(document, errors);
        }
        else
        {
            ValidateRule(document, string.Empty, errors);
        }

        return errors.Count == 0 ? RuleSetValidationResult.Success : new RuleSetValidationResult(errors);
    }

    private static void ValidateRuleSet(RuleJsonValue ruleSet, List<RuleValidationError> errors)
    {
        if (ruleSet.TryGetProperty("name", out RuleJsonValue name) && name.Kind != RuleJsonValueKind.String)
        {
            errors.Add(new RuleValidationError("/name", "'name' must be a string."));
        }

        ruleSet.TryGetProperty("rules", out RuleJsonValue rules);
        if (rules.Kind != RuleJsonValueKind.Array)
        {
            errors.Add(new RuleValidationError("/rules", "'rules' must be an array."));
            return;
        }

        if (rules.Items.Count == 0)
        {
            errors.Add(new RuleValidationError("/rules", "'rules' must contain at least one rule."));
            return;
        }

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < rules.Items.Count; i++)
        {
            string path = "/rules/" + i.ToString(CultureInfo.InvariantCulture);
            ValidateRule(rules.Items[i], path, errors);

            if (rules.Items[i].Kind == RuleJsonValueKind.Object
                && rules.Items[i].TryGetProperty("id", out RuleJsonValue id)
                && id.Kind == RuleJsonValueKind.String
                && !seenIds.Add(id.GetString()))
            {
                errors.Add(new RuleValidationError(path + "/id", $"Duplicate rule id '{id.GetString()}'."));
            }
        }
    }

    private static void ValidateRule(RuleJsonValue rule, string path, List<RuleValidationError> errors)
    {
        if (rule.Kind != RuleJsonValueKind.Object)
        {
            errors.Add(new RuleValidationError(path, "A rule must be a JSON object."));
            return;
        }

        if (!rule.TryGetProperty("id", out RuleJsonValue id))
        {
            errors.Add(new RuleValidationError(path, "'id' is required."));
        }
        else if (id.Kind != RuleJsonValueKind.String || id.GetString().Length == 0)
        {
            errors.Add(new RuleValidationError(path + "/id", "'id' must be a non-empty string."));
        }

        if (rule.TryGetProperty("description", out RuleJsonValue description)
            && description.Kind != RuleJsonValueKind.String)
        {
            errors.Add(new RuleValidationError(path + "/description", "'description' must be a string."));
        }

        if (rule.TryGetProperty("priority", out RuleJsonValue priority)
            && (priority.Kind != RuleJsonValueKind.Number
                || !priority.TryGetInt64(out long priorityValue)
                || priorityValue < int.MinValue
                || priorityValue > int.MaxValue))
        {
            errors.Add(new RuleValidationError(path + "/priority", "'priority' must be a 32-bit integer."));
        }

        if (rule.TryGetProperty("enabled", out RuleJsonValue enabled)
            && enabled.Kind != RuleJsonValueKind.True
            && enabled.Kind != RuleJsonValueKind.False)
        {
            errors.Add(new RuleValidationError(path + "/enabled", "'enabled' must be a boolean."));
        }

        if (!rule.TryGetProperty("condition", out RuleJsonValue condition))
        {
            errors.Add(new RuleValidationError(path, "'condition' is required."));
        }
        else
        {
            ValidateCondition(condition, path + "/condition", errors);
        }

        if (rule.TryGetProperty("actions", out RuleJsonValue actions))
        {
            if (actions.Kind != RuleJsonValueKind.Array)
            {
                errors.Add(new RuleValidationError(path + "/actions", "'actions' must be an array."));
            }
            else
            {
                for (int i = 0; i < actions.Items.Count; i++)
                {
                    ValidateAction(actions.Items[i], path + "/actions/" + i.ToString(CultureInfo.InvariantCulture), errors);
                }
            }
        }

        if (rule.TryGetProperty("layout", out RuleJsonValue layout) && layout.Kind != RuleJsonValueKind.Object)
        {
            errors.Add(new RuleValidationError(path + "/layout", "'layout' must be an object."));
        }
    }

    private static void ValidateCondition(RuleJsonValue condition, string path, List<RuleValidationError> errors)
    {
        if (condition.Kind != RuleJsonValueKind.Object)
        {
            errors.Add(new RuleValidationError(path, "A condition must be a JSON object."));
            return;
        }

        // Discriminator: groups carry "type": "group"; anything else is a leaf.
        if (condition.TryGetProperty("type", out RuleJsonValue type))
        {
            if (type.Kind != RuleJsonValueKind.String || type.GetString() != "group")
            {
                errors.Add(new RuleValidationError(path + "/type", "'type' must be the string \"group\"."));
                return;
            }

            ValidateGroup(condition, path, errors);
        }
        else
        {
            ValidateLeaf(condition, path, errors);
        }
    }

    private static void ValidateGroup(RuleJsonValue group, string path, List<RuleValidationError> errors)
    {
        string? operatorName = null;
        if (!group.TryGetProperty("operator", out RuleJsonValue op))
        {
            errors.Add(new RuleValidationError(path, "'operator' is required for a group."));
        }
        else if (op.Kind != RuleJsonValueKind.String
            || (operatorName = op.GetString()) is not ("AND" or "OR" or "NOT"))
        {
            errors.Add(new RuleValidationError(path + "/operator", "Group 'operator' must be \"AND\", \"OR\", or \"NOT\"."));
            operatorName = null;
        }

        if (!group.TryGetProperty("rules", out RuleJsonValue rules) || rules.Kind != RuleJsonValueKind.Array)
        {
            errors.Add(new RuleValidationError(path, "'rules' (an array of child conditions) is required for a group."));
            return;
        }

        if (rules.Items.Count == 0)
        {
            errors.Add(new RuleValidationError(path + "/rules", "A group must contain at least one child condition."));
            return;
        }

        if (operatorName == "NOT" && rules.Items.Count != 1)
        {
            errors.Add(new RuleValidationError(path + "/rules", "A NOT group must contain exactly one child condition."));
        }

        for (int i = 0; i < rules.Items.Count; i++)
        {
            ValidateCondition(rules.Items[i], path + "/rules/" + i.ToString(CultureInfo.InvariantCulture), errors);
        }
    }

    private static void ValidateLeaf(RuleJsonValue leaf, string path, List<RuleValidationError> errors)
    {
        if (!leaf.TryGetProperty("operator", out RuleJsonValue op) || op.Kind != RuleJsonValueKind.String)
        {
            errors.Add(new RuleValidationError(path, "'operator' (a string) is required for a condition."));
            return;
        }

        if (!OperatorMap.TryParse(op.GetString(), out Core.ConditionOperator parsedOperator))
        {
            errors.Add(new RuleValidationError(
                path + "/operator",
                $"Unknown operator '{op.GetString()}'. Expected one of: {string.Join(", ", OperatorMap.JsonNames)}."));
            return;
        }

        bool hasField = leaf.TryGetProperty("field", out RuleJsonValue field);
        if (hasField && (field.Kind != RuleJsonValueKind.String || field.GetString().Length == 0))
        {
            errors.Add(new RuleValidationError(path + "/field", "'field' must be a non-empty string."));
            hasField = false;
        }

        if (parsedOperator == Core.ConditionOperator.Custom)
        {
            if (!leaf.TryGetProperty("name", out RuleJsonValue name)
                || name.Kind != RuleJsonValueKind.String
                || name.GetString().Length == 0)
            {
                errors.Add(new RuleValidationError(path, "'name' (a non-empty string) is required when operator is \"custom\"."));
            }

            return;
        }

        if (!hasField)
        {
            errors.Add(new RuleValidationError(path, $"'field' is required for operator '{op.GetString()}'."));
        }

        bool hasValue = leaf.TryGetProperty("value", out RuleJsonValue value);
        switch (parsedOperator)
        {
            case Core.ConditionOperator.IsNull:
            case Core.ConditionOperator.IsNotNull:
                if (hasValue)
                {
                    errors.Add(new RuleValidationError(path + "/value", $"'value' is not allowed for operator '{op.GetString()}'."));
                }

                break;

            case Core.ConditionOperator.In:
            case Core.ConditionOperator.NotIn:
                if (!hasValue || value.Kind != RuleJsonValueKind.Array)
                {
                    errors.Add(new RuleValidationError(
                        hasValue ? path + "/value" : path,
                        $"'value' must be a non-empty array for operator '{op.GetString()}'."));
                }
                else if (value.Items.Count == 0)
                {
                    errors.Add(new RuleValidationError(path + "/value", $"'value' must be a non-empty array for operator '{op.GetString()}'."));
                }
                else
                {
                    for (int i = 0; i < value.Items.Count; i++)
                    {
                        RuleJsonValueKind itemKind = value.Items[i].Kind;
                        if (itemKind is RuleJsonValueKind.Object or RuleJsonValueKind.Array or RuleJsonValueKind.Null)
                        {
                            errors.Add(new RuleValidationError(
                                path + "/value/" + i.ToString(CultureInfo.InvariantCulture),
                                "In/NotIn values must be scalars (string, number, or boolean)."));
                        }
                    }
                }

                break;

            case Core.ConditionOperator.Contains:
            case Core.ConditionOperator.StartsWith:
            case Core.ConditionOperator.EndsWith:
                if (!hasValue || value.Kind != RuleJsonValueKind.String)
                {
                    errors.Add(new RuleValidationError(
                        hasValue ? path + "/value" : path,
                        $"'value' must be a string for operator '{op.GetString()}'."));
                }

                break;

            case Core.ConditionOperator.MatchesRegex:
                if (!hasValue || value.Kind != RuleJsonValueKind.String)
                {
                    errors.Add(new RuleValidationError(
                        hasValue ? path + "/value" : path,
                        "'value' must be a string (a regular expression) for operator 'MatchesRegex'."));
                }
                else
                {
                    try
                    {
                        _ = new Regex(value.GetString());
                    }
                    catch (ArgumentException ex)
                    {
                        errors.Add(new RuleValidationError(path + "/value", $"Invalid regular expression: {ex.Message}"));
                    }
                }

                break;

            case Core.ConditionOperator.GreaterThan:
            case Core.ConditionOperator.GreaterThanOrEqual:
            case Core.ConditionOperator.LessThan:
            case Core.ConditionOperator.LessThanOrEqual:
                if (!hasValue || value.Kind is not (RuleJsonValueKind.Number or RuleJsonValueKind.String))
                {
                    errors.Add(new RuleValidationError(
                        hasValue ? path + "/value" : path,
                        $"'value' must be a number or string for operator '{op.GetString()}'."));
                }

                break;

            case Core.ConditionOperator.Equal:
            case Core.ConditionOperator.NotEqual:
                if (!hasValue)
                {
                    errors.Add(new RuleValidationError(path, $"'value' is required for operator '{op.GetString()}'."));
                }
                else if (value.Kind is RuleJsonValueKind.Object or RuleJsonValueKind.Array)
                {
                    errors.Add(new RuleValidationError(path + "/value", $"'value' must be a scalar or null for operator '{op.GetString()}'."));
                }

                break;
        }
    }

    private static void ValidateAction(RuleJsonValue action, string path, List<RuleValidationError> errors)
    {
        if (action.Kind != RuleJsonValueKind.Object)
        {
            errors.Add(new RuleValidationError(path, "An action must be a JSON object."));
            return;
        }

        if (!action.TryGetProperty("type", out RuleJsonValue type)
            || type.Kind != RuleJsonValueKind.String
            || type.GetString() != Core.RuleAction.SetOutputType)
        {
            errors.Add(new RuleValidationError(
                action.TryGetProperty("type", out _) ? path + "/type" : path,
                $"Action 'type' must be \"{Core.RuleAction.SetOutputType}\" (the only action type in v1)."));
        }

        if (!action.TryGetProperty("target", out RuleJsonValue target)
            || target.Kind != RuleJsonValueKind.String
            || target.GetString().Length == 0)
        {
            errors.Add(new RuleValidationError(
                action.TryGetProperty("target", out _) ? path + "/target" : path,
                "Action 'target' must be a non-empty string."));
        }

        if (!action.TryGetProperty("value", out RuleJsonValue value))
        {
            errors.Add(new RuleValidationError(path, "Action 'value' is required (it may be null)."));
        }
        else if (value.Kind is RuleJsonValueKind.Object or RuleJsonValueKind.Array)
        {
            errors.Add(new RuleValidationError(path + "/value", "Action 'value' must be a scalar or null in v1."));
        }
    }
}
