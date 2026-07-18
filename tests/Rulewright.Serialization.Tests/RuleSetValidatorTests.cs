using System.Linq;
using Rulewright.Json.SystemText;
using Rulewright.Serialization;
using Xunit;

namespace Rulewright.Serialization.Tests;

public class RuleSetValidatorTests
{
    private static RuleSetValidationResult Validate(string json)
        => RuleSetValidator.Validate(new SystemTextJsonReader().Read(json));

    private const string SpecExample = @"{
      ""id"": ""discount-rule-01"",
      ""description"": ""VIP or high-value customers get 10% off"",
      ""priority"": 10,
      ""enabled"": true,
      ""condition"": {
        ""type"": ""group"",
        ""operator"": ""AND"",
        ""rules"": [
          { ""field"": ""Customer.Age"", ""operator"": ""GreaterThan"", ""value"": 18 },
          {
            ""type"": ""group"",
            ""operator"": ""OR"",
            ""rules"": [
              { ""field"": ""Order.Total"", ""operator"": ""GreaterThanOrEqual"", ""value"": 100 },
              { ""field"": ""Customer.IsVip"", ""operator"": ""Equals"", ""value"": true }
            ]
          }
        ]
      },
      ""actions"": [
        { ""type"": ""setOutput"", ""target"": ""Discount"", ""value"": 10 },
        { ""type"": ""setOutput"", ""target"": ""DiscountReason"", ""value"": ""VIP or high-value order"" }
      ],
      ""layout"": {
        ""position"": { ""x"": 120, ""y"": 40 },
        ""nodeIds"": { ""condition-root"": ""n1"", ""action-0"": ""n2"" }
      }
    }";

    [Fact]
    public void SpecExample_IsValid()
    {
        RuleSetValidationResult result = Validate(SpecExample);
        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.ToString())));
    }

    [Fact]
    public void NonObjectRoot_ErrorAtRoot()
    {
        RuleSetValidationResult result = Validate("[1, 2]");
        Assert.False(result.IsValid);
        Assert.Equal(string.Empty, result.Errors.Single().Path);
    }

    [Fact]
    public void MissingId_IsReported()
    {
        RuleSetValidationResult result = Validate(
            "{\"condition\": {\"field\": \"A\", \"operator\": \"IsNull\"}}");
        Assert.Contains(result.Errors, e => e.Message.Contains("'id'"));
    }

    [Fact]
    public void UnknownOperator_PointerTargetsNestedNode()
    {
        RuleSetValidationResult result = Validate(@"{
          ""id"": ""r"",
          ""condition"": {
            ""type"": ""group"", ""operator"": ""AND"",
            ""rules"": [
              { ""field"": ""A"", ""operator"": ""IsNull"" },
              { ""field"": ""B"", ""operator"": ""LooksLike"", ""value"": 1 }
            ]
          }
        }");
        RuleValidationError error = Assert.Single(result.Errors);
        Assert.Equal("/condition/rules/1/operator", error.Path);
    }

    [Fact]
    public void NotGroup_WithTwoChildren_IsReported()
    {
        RuleSetValidationResult result = Validate(@"{
          ""id"": ""r"",
          ""condition"": {
            ""type"": ""group"", ""operator"": ""NOT"",
            ""rules"": [
              { ""field"": ""A"", ""operator"": ""IsNull"" },
              { ""field"": ""B"", ""operator"": ""IsNull"" }
            ]
          }
        }");
        Assert.Contains(result.Errors, e => e.Path == "/condition/rules" && e.Message.Contains("exactly one"));
    }

    [Fact]
    public void InOperator_RequiresNonEmptyArray()
    {
        Assert.False(Validate("{\"id\":\"r\",\"condition\":{\"field\":\"A\",\"operator\":\"In\",\"value\":5}}").IsValid);
        Assert.False(Validate("{\"id\":\"r\",\"condition\":{\"field\":\"A\",\"operator\":\"In\",\"value\":[]}}").IsValid);
        Assert.True(Validate("{\"id\":\"r\",\"condition\":{\"field\":\"A\",\"operator\":\"In\",\"value\":[1,2]}}").IsValid);
    }

    [Fact]
    public void IsNull_WithValue_IsReported()
    {
        RuleSetValidationResult result = Validate(
            "{\"id\":\"r\",\"condition\":{\"field\":\"A\",\"operator\":\"IsNull\",\"value\":1}}");
        Assert.Contains(result.Errors, e => e.Path == "/condition/value");
    }

    [Fact]
    public void CustomOperator_RequiresName()
    {
        Assert.False(Validate("{\"id\":\"r\",\"condition\":{\"operator\":\"custom\"}}").IsValid);
        Assert.True(Validate("{\"id\":\"r\",\"condition\":{\"operator\":\"custom\",\"name\":\"F\"}}").IsValid);
    }

    [Fact]
    public void InvalidRegex_IsReported()
    {
        RuleSetValidationResult result = Validate(
            "{\"id\":\"r\",\"condition\":{\"field\":\"A\",\"operator\":\"MatchesRegex\",\"value\":\"[unclosed\"}}");
        Assert.Contains(result.Errors, e => e.Path == "/condition/value" && e.Message.Contains("regular expression"));
    }

    [Fact]
    public void FractionalPriority_IsReported()
    {
        RuleSetValidationResult result = Validate(
            "{\"id\":\"r\",\"priority\":1.5,\"condition\":{\"field\":\"A\",\"operator\":\"IsNull\"}}");
        Assert.Contains(result.Errors, e => e.Path == "/priority");
    }

    [Fact]
    public void UnknownActionType_IsReported()
    {
        RuleSetValidationResult result = Validate(@"{
          ""id"": ""r"",
          ""condition"": { ""field"": ""A"", ""operator"": ""IsNull"" },
          ""actions"": [ { ""type"": ""sendEmail"", ""target"": ""T"", ""value"": 1 } ]
        }");
        Assert.Contains(result.Errors, e => e.Path == "/actions/0/type");
    }

    [Fact]
    public void RuleSet_DuplicateIds_AreReported()
    {
        RuleSetValidationResult result = Validate(@"{
          ""rules"": [
            { ""id"": ""a"", ""condition"": { ""field"": ""X"", ""operator"": ""IsNull"" } },
            { ""id"": ""a"", ""condition"": { ""field"": ""Y"", ""operator"": ""IsNull"" } }
          ]
        }");
        Assert.Contains(result.Errors, e => e.Path == "/rules/1/id" && e.Message.Contains("Duplicate"));
    }

    [Fact]
    public void RuleSet_ErrorsCarryRuleIndexInPointer()
    {
        RuleSetValidationResult result = Validate(@"{
          ""rules"": [
            { ""id"": ""a"", ""condition"": { ""field"": ""X"", ""operator"": ""IsNull"" } },
            { ""id"": ""b"", ""condition"": { ""field"": ""Y"", ""operator"": ""Nope"" } }
          ]
        }");
        RuleValidationError error = Assert.Single(result.Errors);
        Assert.Equal("/rules/1/condition/operator", error.Path);
    }
}
