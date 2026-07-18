using System;
using System.Globalization;
using System.Linq;
using Rulewright.Core;

namespace Rulewright.Execution;

/// <summary>
/// Renders human-readable descriptions of condition nodes for execution traces,
/// e.g. <c>"Customer.Age GreaterThan 18"</c> or <c>"AND"</c>.
/// </summary>
internal static class ConditionDescriber
{
    internal static string Describe(ConditionNode node)
    {
        if (node is ConditionGroup group)
        {
            return group.Operator switch
            {
                LogicalOperator.And => "AND",
                LogicalOperator.Or => "OR",
                _ => "NOT",
            };
        }

        var leaf = (ConditionLeaf)node;
        string field = leaf.Field ?? "(fact)";
        return leaf.Operator switch
        {
            ConditionOperator.Custom => $"{field} custom:{leaf.FunctionName}",
            ConditionOperator.IsNull => $"{field} IsNull",
            ConditionOperator.IsNotNull => $"{field} IsNotNull",
            _ => $"{field} {OperatorName(leaf.Operator)} {Literal(leaf.Value)}",
        };
    }

    private static string OperatorName(ConditionOperator @operator) => @operator switch
    {
        ConditionOperator.Equal => "Equals",
        ConditionOperator.NotEqual => "NotEquals",
        ConditionOperator.GreaterThan => "GreaterThan",
        ConditionOperator.GreaterThanOrEqual => "GreaterThanOrEqual",
        ConditionOperator.LessThan => "LessThan",
        ConditionOperator.LessThanOrEqual => "LessThanOrEqual",
        ConditionOperator.Contains => "Contains",
        ConditionOperator.StartsWith => "StartsWith",
        ConditionOperator.EndsWith => "EndsWith",
        ConditionOperator.MatchesRegex => "MatchesRegex",
        ConditionOperator.In => "In",
        _ => "NotIn",
    };

    private static string Literal(object? value) => value switch
    {
        null => "null",
        string s => "\"" + s + "\"",
        bool b => b ? "true" : "false",
        object?[] array => "[" + string.Join(", ", array.Select(Literal)) + "]",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "null",
    };
}
