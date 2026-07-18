using Rulewright.Core;
using Rulewright.Json.SystemText;
using Rulewright.Serialization;
using Xunit;

namespace Rulewright.Serialization.Tests;

public class RuleHasherTests
{
    private static Rule ParseRule(string json)
        => RuleSetParser.Parse(new SystemTextJsonReader().Read(json)).Rules[0];

    [Fact]
    public void Hash_IgnoresWhitespaceAndKeyOrder()
    {
        Rule compact = ParseRule(
            "{\"id\":\"r\",\"condition\":{\"field\":\"A\",\"operator\":\"Equals\",\"value\":1},\"actions\":[{\"type\":\"setOutput\",\"target\":\"T\",\"value\":true}]}");
        Rule reordered = ParseRule(@"{
          ""actions"": [ { ""value"": true, ""target"": ""T"", ""type"": ""setOutput"" } ],
          ""condition"": { ""value"": 1, ""operator"": ""Equals"", ""field"": ""A"" },
          ""id"": ""r""
        }");

        Assert.Equal(RuleHasher.ComputeHash(compact), RuleHasher.ComputeHash(reordered));
    }

    [Fact]
    public void Hash_IgnoresLayoutDescriptionPriorityEnabledAndId()
    {
        Rule bare = ParseRule(
            "{\"id\":\"r1\",\"condition\":{\"field\":\"A\",\"operator\":\"Equals\",\"value\":1}}");
        Rule decorated = ParseRule(@"{
          ""id"": ""r2-different"",
          ""description"": ""different description"",
          ""priority"": 99,
          ""enabled"": false,
          ""condition"": { ""field"": ""A"", ""operator"": ""Equals"", ""value"": 1 },
          ""layout"": { ""position"": { ""x"": 5, ""y"": 6 } }
        }");

        Assert.Equal(RuleHasher.ComputeHash(bare), RuleHasher.ComputeHash(decorated));
    }

    [Fact]
    public void Hash_ChangesWhenConditionValueChanges()
    {
        Rule one = ParseRule("{\"id\":\"r\",\"condition\":{\"field\":\"A\",\"operator\":\"Equals\",\"value\":1}}");
        Rule two = ParseRule("{\"id\":\"r\",\"condition\":{\"field\":\"A\",\"operator\":\"Equals\",\"value\":2}}");
        Assert.NotEqual(RuleHasher.ComputeHash(one), RuleHasher.ComputeHash(two));
    }

    [Fact]
    public void Hash_ChangesWhenActionsChange()
    {
        Rule one = ParseRule(
            "{\"id\":\"r\",\"condition\":{\"field\":\"A\",\"operator\":\"IsNull\"},\"actions\":[{\"type\":\"setOutput\",\"target\":\"T\",\"value\":1}]}");
        Rule two = ParseRule(
            "{\"id\":\"r\",\"condition\":{\"field\":\"A\",\"operator\":\"IsNull\"},\"actions\":[{\"type\":\"setOutput\",\"target\":\"T\",\"value\":2}]}");
        Assert.NotEqual(RuleHasher.ComputeHash(one), RuleHasher.ComputeHash(two));
    }

    [Fact]
    public void Hash_TreatsNumericallyEqualDecimalsAsEqual()
    {
        Rule plain = ParseRule("{\"id\":\"r\",\"condition\":{\"field\":\"A\",\"operator\":\"Equals\",\"value\":10.5}}");
        Rule trailingZero = ParseRule("{\"id\":\"r\",\"condition\":{\"field\":\"A\",\"operator\":\"Equals\",\"value\":10.50}}");
        Assert.Equal(RuleHasher.ComputeHash(plain), RuleHasher.ComputeHash(trailingZero));
    }

    [Fact]
    public void CanonicalForm_IsDeterministicJson()
    {
        Rule rule = ParseRule(
            "{\"id\":\"r\",\"condition\":{\"field\":\"A\",\"operator\":\"Equals\",\"value\":\"x\"},\"actions\":[{\"type\":\"setOutput\",\"target\":\"T\",\"value\":10}]}");
        Assert.Equal(
            "{\"actions\":[{\"target\":\"T\",\"type\":\"setOutput\",\"value\":10}],"
            + "\"condition\":{\"field\":\"A\",\"operator\":\"Equals\",\"value\":\"x\"}}",
            RuleHasher.GetCanonicalForm(rule));
    }
}
