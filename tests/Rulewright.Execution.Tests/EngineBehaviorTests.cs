using Rulewright.Core;
using Rulewright.Serialization;
using Xunit;
using static Rulewright.Execution.Tests.TestEngine;

namespace Rulewright.Execution.Tests;

public class EngineBehaviorTests
{
    private const string TwoRuleSet = @"{
      ""rules"": [
        {
          ""id"": ""low"", ""priority"": 1,
          ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""GreaterThan"", ""value"": 18 },
          ""actions"": [ { ""type"": ""setOutput"", ""target"": ""Discount"", ""value"": 5 } ]
        },
        {
          ""id"": ""high"", ""priority"": 10,
          ""condition"": { ""field"": ""Customer.IsVip"", ""operator"": ""Equals"", ""value"": true },
          ""actions"": [ { ""type"": ""setOutput"", ""target"": ""Discount"", ""value"": 10 } ]
        }
      ]
    }";

    [Fact]
    public void HigherPriorityRulesEvaluateFirst()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(TwoRuleSet);
        RuleEvaluationResult result = Engine.Evaluate(loaded, DefaultFact());

        Assert.Equal(new[] { "high", "low" }, result.FiredRules.Select(r => r.RuleId).ToArray());
        // Both fired; the later (lower-priority) rule's output wins the merge.
        Assert.Equal(5L, result.Outputs["Discount"]);
    }

    [Fact]
    public void StopOnFirstMatch_SkipsRemainingRules()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(TwoRuleSet);
        RuleEvaluationResult result = Engine.Evaluate(
            loaded, DefaultFact(), new EvaluationOptions { StopOnFirstMatch = true, EnableTrace = true });

        FiredRule fired = Assert.Single(result.FiredRules);
        Assert.Equal("high", fired.RuleId);
        Assert.Equal(10L, result.Outputs["Discount"]);

        RuleTrace lowTrace = result.Trace!.Rules.Single(r => r.RuleId == "low");
        Assert.True(lowTrace.Skipped);
        Assert.False(lowTrace.Fired);
    }

    [Fact]
    public void EqualPriority_KeepsDocumentOrder()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(@"{
          ""rules"": [
            { ""id"": ""first"", ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""IsNotNull"" } },
            { ""id"": ""second"", ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""IsNotNull"" } }
          ]
        }");
        RuleEvaluationResult result = Engine.Evaluate(loaded, DefaultFact());
        Assert.Equal(new[] { "first", "second" }, result.FiredRules.Select(r => r.RuleId).ToArray());
    }

    [Fact]
    public void DisabledRules_AreSkipped()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(@"{
          ""rules"": [
            { ""id"": ""off"", ""enabled"": false, ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""IsNotNull"" } },
            { ""id"": ""on"", ""condition"": { ""field"": ""Customer.Age"", ""operator"": ""IsNotNull"" } }
          ]
        }");
        RuleEvaluationResult result = Engine.Evaluate(loaded, DefaultFact(), new EvaluationOptions { EnableTrace = true });

        Assert.Equal("on", Assert.Single(result.FiredRules).RuleId);
        RuleTrace offTrace = result.Trace!.Rules.Single(r => r.RuleId == "off");
        Assert.True(offTrace.Skipped);
        Assert.Null(offTrace.Condition);
    }

    [Fact]
    public void NoMatch_ReturnsEmptyResult()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(WrapRule("{\"field\":\"Customer.Age\",\"operator\":\"GreaterThan\",\"value\":99}"));
        RuleEvaluationResult result = Engine.Evaluate(loaded, DefaultFact());
        Assert.Empty(result.FiredRules);
        Assert.Empty(result.Outputs);
    }

    [Fact]
    public void ChangedRuleBody_SameId_IsRecompiledNotServedStale()
    {
        LoadedRuleSet v1 = Engine.LoadRuleSet(WrapRule("{\"field\":\"Customer.Age\",\"operator\":\"GreaterThan\",\"value\":100}"));
        Assert.Empty(Engine.Evaluate(v1, DefaultFact()).FiredRules);

        // Same rule id, different body: the content-hash cache key must differ.
        LoadedRuleSet v2 = Engine.LoadRuleSet(WrapRule("{\"field\":\"Customer.Age\",\"operator\":\"GreaterThan\",\"value\":10}"));
        Assert.Single(Engine.Evaluate(v2, DefaultFact()).FiredRules);
    }

    [Fact]
    public void ConcurrentEvaluations_AreConsistent()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(TwoRuleSet);
        OrderFact fact = DefaultFact();

        Parallel.For(0, 500, i =>
        {
            var options = new EvaluationOptions { EnableTrace = i % 2 == 0 };
            RuleEvaluationResult result = Engine.Evaluate(loaded, fact, options);
            Assert.Equal(2, result.FiredRules.Count);
            Assert.Equal(5L, result.Outputs["Discount"]);
        });
    }

    [Fact]
    public void NullFact_Throws()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(WrapRule("{\"field\":\"Customer.Age\",\"operator\":\"IsNotNull\"}"));
        Assert.Throws<ArgumentNullException>(() => Engine.Evaluate<OrderFact>(loaded, null!));
    }

    [Fact]
    public void EngineWithoutReader_RejectsJsonButAcceptsDomainRuleSets()
    {
        RulewrightEngine engine = new RulewrightBuilder().Build();
        Assert.Throws<InvalidOperationException>(() => engine.LoadRuleSet("{}"));

        var rule = new Rule("r", new ConditionLeaf("Customer.Age", ConditionOperator.GreaterThan, 18L));
        LoadedRuleSet loaded = engine.LoadRuleSet(new RuleSet(new[] { rule }));
        Assert.Single(engine.Evaluate(loaded, DefaultFact()).FiredRules);
    }

    [Fact]
    public void Validate_ReturnsPointerErrorsWithoutThrowing()
    {
        RuleSetValidationResult malformed = Engine.Validate("{ not json");
        Assert.False(malformed.IsValid);
        Assert.Equal(string.Empty, malformed.Errors.Single().Path);

        RuleSetValidationResult invalid = Engine.Validate("{\"id\":\"r\",\"condition\":{\"field\":\"A\",\"operator\":\"Nope\",\"value\":1}}");
        Assert.False(invalid.IsValid);
        Assert.Equal("/condition/operator", invalid.Errors.Single().Path);

        Assert.True(Engine.Validate(WrapRule("{\"field\":\"A\",\"operator\":\"IsNull\"}")).IsValid);
    }

    [Fact]
    public void FiredRuleOutputs_ArePerRule()
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(TwoRuleSet);
        RuleEvaluationResult result = Engine.Evaluate(loaded, DefaultFact());
        Assert.Equal(10L, result.FiredRules[0].Outputs["Discount"]);
        Assert.Equal(5L, result.FiredRules[1].Outputs["Discount"]);
    }
}
