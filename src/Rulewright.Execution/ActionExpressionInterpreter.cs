using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Rulewright.Core;

namespace Rulewright.Execution;

/// <summary>
/// Produces a rule's outputs for dictionary facts, evaluating computed-action value
/// expressions by walking the expression tree directly. Mirrors the compiled path's
/// output production (<see cref="RuleExpressionCompiler"/>) exactly: field reads use the
/// same <see cref="RuleInterpreter.ResolvePath"/> resolution, operators the same
/// <see cref="ValueExpressionOps"/> semantics, and later actions overwrite earlier ones
/// on the same target.
/// </summary>
internal static class ActionExpressionInterpreter
{
    internal static IReadOnlyDictionary<string, object?> Produce(IReadOnlyList<RuleAction> actions, object fact)
    {
        var outputs = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (RuleAction action in actions)
        {
            outputs[action.Target] = Evaluate(action.Value, fact);
        }

        return new ReadOnlyDictionary<string, object?>(outputs);
    }

    private static object? Evaluate(ValueExpression expression, object fact)
    {
        switch (expression)
        {
            case LiteralExpression literal:
                return literal.Value;

            case FieldExpression field:
                return RuleInterpreter.ResolvePath(fact, field.Path);

            case OperatorExpression op:
                return EvaluateOperator(op, fact);

            default:
                return null;
        }
    }

    private static object? EvaluateOperator(OperatorExpression op, object fact)
    {
        IReadOnlyList<ValueExpression> operands = op.Operands;
        switch (op.Operator)
        {
            case ExpressionOperator.Add:
                return Fold(operands, fact, ValueExpressionOps.Add);

            case ExpressionOperator.Multiply:
                return Fold(operands, fact, ValueExpressionOps.Multiply);

            case ExpressionOperator.Subtract:
                return ValueExpressionOps.Subtract(Evaluate(operands[0], fact), Evaluate(operands[1], fact));

            case ExpressionOperator.Divide:
                return ValueExpressionOps.Divide(Evaluate(operands[0], fact), Evaluate(operands[1], fact));

            case ExpressionOperator.Modulo:
                return ValueExpressionOps.Modulo(Evaluate(operands[0], fact), Evaluate(operands[1], fact));

            case ExpressionOperator.Negate:
                return ValueExpressionOps.Negate(Evaluate(operands[0], fact));

            case ExpressionOperator.Concat:
                return ValueExpressionOps.Concat(EvaluateAll(operands, fact));

            default: // Coalesce
                return ValueExpressionOps.Coalesce(EvaluateAll(operands, fact));
        }
    }

    private static object? Fold(IReadOnlyList<ValueExpression> operands, object fact, Func<object?, object?, object?> op)
    {
        object? accumulator = Evaluate(operands[0], fact);
        for (int i = 1; i < operands.Count; i++)
        {
            accumulator = op(accumulator, Evaluate(operands[i], fact));
        }

        return accumulator;
    }

    private static object?[] EvaluateAll(IReadOnlyList<ValueExpression> operands, object fact)
    {
        var values = new object?[operands.Count];
        for (int i = 0; i < operands.Count; i++)
        {
            values[i] = Evaluate(operands[i], fact);
        }

        return values;
    }
}
