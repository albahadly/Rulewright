using System;
using Rulewright.Core;
using Xunit;

namespace Rulewright.Core.Tests;

public class ValueExpressionTests
{
    [Fact]
    public void Literal_HoldsValueIncludingNull()
    {
        Assert.Equal(10L, new LiteralExpression(10L).Value);
        Assert.Null(new LiteralExpression(null).Value);
    }

    [Fact]
    public void Field_EmptyPath_Throws()
    {
        Assert.Throws<ArgumentException>(() => new FieldExpression(string.Empty));
        Assert.Throws<ArgumentException>(() => new FieldExpression(null!));
    }

    [Fact]
    public void Operator_MaterializesOperandsInOrder()
    {
        var op = new OperatorExpression(
            ExpressionOperator.Subtract,
            new ValueExpression[] { new FieldExpression("A"), new LiteralExpression(1L) });

        Assert.Equal(ExpressionOperator.Subtract, op.Operator);
        Assert.Equal(2, op.Operands.Count);
        Assert.IsType<FieldExpression>(op.Operands[0]);
        Assert.IsType<LiteralExpression>(op.Operands[1]);
    }

    [Fact]
    public void Operator_NoOperands_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new OperatorExpression(ExpressionOperator.Add, Array.Empty<ValueExpression>()));
    }

    [Fact]
    public void Operator_NullOperand_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new OperatorExpression(ExpressionOperator.Add, new ValueExpression[] { null! }));
    }

    [Fact]
    public void Action_FromExpression_HoldsIt()
    {
        var expression = new FieldExpression("Order.Total");
        var action = new RuleAction(RuleAction.SetOutputType, "Discount", expression);

        Assert.Same(expression, action.Value);
    }

    [Fact]
    public void Action_FromScalar_WrapsInLiteral()
    {
        var action = new RuleAction(RuleAction.SetOutputType, "Discount", 10L);

        Assert.Equal(10L, Assert.IsType<LiteralExpression>(action.Value).Value);
    }

    [Fact]
    public void Action_NullExpression_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new RuleAction(RuleAction.SetOutputType, "Discount", (ValueExpression)null!));
    }
}
