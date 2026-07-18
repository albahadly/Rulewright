using Rulewright.Core;
using Xunit;

namespace Rulewright.Core.Tests;

public class DomainModelTests
{
    private static ConditionLeaf Leaf() => new ConditionLeaf("A", ConditionOperator.Equal, 1L);

    [Fact]
    public void Rule_Defaults_PriorityZeroAndEnabled()
    {
        var rule = new Rule("r1", Leaf());
        Assert.Equal(0, rule.Priority);
        Assert.True(rule.Enabled);
        Assert.Null(rule.Description);
        Assert.Empty(rule.Actions);
    }

    [Fact]
    public void Rule_EmptyId_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Rule(string.Empty, Leaf()));
    }

    [Fact]
    public void Rule_NullCondition_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new Rule("r1", null!));
    }

    [Fact]
    public void ConditionGroup_NotWithTwoChildren_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new ConditionGroup(LogicalOperator.Not, new ConditionNode[] { Leaf(), Leaf() }));
    }

    [Fact]
    public void ConditionGroup_Empty_Throws()
    {
        Assert.Throws<ArgumentException>(() => new ConditionGroup(LogicalOperator.And, new ConditionNode[0]));
    }

    [Fact]
    public void ConditionLeaf_CustomWithoutFunctionName_Throws()
    {
        Assert.Throws<ArgumentException>(() => new ConditionLeaf("A", ConditionOperator.Custom, null));
    }

    [Fact]
    public void ConditionLeaf_NonCustomWithoutField_Throws()
    {
        Assert.Throws<ArgumentException>(() => new ConditionLeaf((string?)null, ConditionOperator.Equal, 1L));
    }

    [Fact]
    public void ConditionLeaf_CustomWithoutField_IsAllowed()
    {
        var leaf = new ConditionLeaf(null, ConditionOperator.Custom, null, "IsBusinessDay");
        Assert.Null(leaf.Field);
        Assert.Equal("IsBusinessDay", leaf.FunctionName);
    }

    [Fact]
    public void RuleSet_DuplicateIds_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new RuleSet(new[] { new Rule("a", Leaf()), new Rule("a", Leaf()) }));
    }

    [Fact]
    public void RuleSet_Empty_Throws()
    {
        Assert.Throws<ArgumentException>(() => new RuleSet(new Rule[0]));
    }

    [Fact]
    public void RuleAction_EmptyTarget_Throws()
    {
        Assert.Throws<ArgumentException>(() => new RuleAction(RuleAction.SetOutputType, string.Empty, 1));
    }

    [Fact]
    public void EvaluationOptions_Default_TracingOffEvaluateAll()
    {
        Assert.False(EvaluationOptions.Default.EnableTrace);
        Assert.False(EvaluationOptions.Default.StopOnFirstMatch);
    }
}
