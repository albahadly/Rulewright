using System;
using System.Globalization;
using System.Text;

namespace Rulewright.Execution;

/// <summary>
/// The runtime semantics of computed-action <see cref="Core.OperatorExpression"/> operators,
/// operating on boxed <c>object?</c> operands. Both execution paths funnel through here —
/// the compiled path emits calls to these methods, the interpreter calls them directly —
/// so typed facts and dictionary facts produce identical outputs by construction.
///
/// Evaluation is total: operators never throw on data. Any null operand propagates to a
/// null result (except <see cref="Coalesce"/>), a non-numeric operand to an arithmetic
/// operator yields null, and division or modulo by zero yields null. Numeric operations
/// run in <see cref="decimal"/> unless a binary floating-point operand forces
/// <see cref="double"/>.
/// </summary>
internal static class ValueExpressionOps
{
    internal static object? Add(object? a, object? b) => Arithmetic(a, b, (x, y) => x + y, (x, y) => x + y);

    internal static object? Subtract(object? a, object? b) => Arithmetic(a, b, (x, y) => x - y, (x, y) => x - y);

    internal static object? Multiply(object? a, object? b) => Arithmetic(a, b, (x, y) => x * y, (x, y) => x * y);

    internal static object? Divide(object? a, object? b) => Arithmetic(
        a,
        b,
        (x, y) => y == 0m ? (decimal?)null : x / y,
        (x, y) => y == 0d ? (double?)null : x / y);

    internal static object? Modulo(object? a, object? b) => Arithmetic(
        a,
        b,
        (x, y) => y == 0m ? (decimal?)null : x % y,
        (x, y) => y == 0d ? (double?)null : x % y);

    internal static object? Negate(object? a)
    {
        if (a is null || !ValueConverter.IsNumericValue(a))
        {
            return null;
        }

        try
        {
            return IsFloating(a)
                ? (object)(-Convert.ToDouble(a, CultureInfo.InvariantCulture))
                : -Convert.ToDecimal(a, CultureInfo.InvariantCulture);
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    internal static object? Concat(object?[] operands)
    {
        var builder = new StringBuilder();
        foreach (object? operand in operands)
        {
            if (operand is null)
            {
                return null;
            }

            builder.Append(Stringify(operand));
        }

        return builder.ToString();
    }

    internal static object? Coalesce(object?[] operands)
    {
        foreach (object? operand in operands)
        {
            if (operand is not null)
            {
                return operand;
            }
        }

        return null;
    }

    private static object? Arithmetic(
        object? a,
        object? b,
        Func<decimal, decimal, decimal?> onDecimal,
        Func<double, double, double?> onDouble)
    {
        if (a is null || b is null || !ValueConverter.IsNumericValue(a) || !ValueConverter.IsNumericValue(b))
        {
            return null;
        }

        try
        {
            if (IsFloating(a) || IsFloating(b))
            {
                double? result = onDouble(
                    Convert.ToDouble(a, CultureInfo.InvariantCulture),
                    Convert.ToDouble(b, CultureInfo.InvariantCulture));
                return result;
            }

            decimal? decimalResult = onDecimal(
                Convert.ToDecimal(a, CultureInfo.InvariantCulture),
                Convert.ToDecimal(b, CultureInfo.InvariantCulture));
            return decimalResult;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static bool IsFloating(object value) => value is double or float;

    private static string Stringify(object value) => value switch
    {
        string s => s,
        bool b => b ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
