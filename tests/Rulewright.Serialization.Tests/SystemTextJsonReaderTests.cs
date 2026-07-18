using Rulewright.Json.SystemText;
using Rulewright.Serialization;
using Xunit;

namespace Rulewright.Serialization.Tests;

public class SystemTextJsonReaderTests
{
    private readonly SystemTextJsonReader _reader = new SystemTextJsonReader();

    [Fact]
    public void Read_Object_PreservesStructureAndKinds()
    {
        RuleJsonValue root = _reader.Read("{\"a\": 1, \"b\": \"x\", \"c\": [true, null], \"d\": {\"e\": 10.5}}");

        Assert.Equal(RuleJsonValueKind.Object, root.Kind);
        Assert.True(root.TryGetProperty("a", out RuleJsonValue a));
        Assert.Equal(1L, a.ToClrValue());
        Assert.True(root.TryGetProperty("b", out RuleJsonValue b));
        Assert.Equal("x", b.ToClrValue());
        Assert.True(root.TryGetProperty("c", out RuleJsonValue c));
        Assert.Equal(RuleJsonValueKind.Array, c.Kind);
        Assert.True((bool)c.Items[0].ToClrValue()!);
        Assert.Null(c.Items[1].ToClrValue());
        Assert.True(root.TryGetProperty("d", out RuleJsonValue d));
        Assert.True(d.TryGetProperty("e", out RuleJsonValue e));
        Assert.Equal(10.5m, e.ToClrValue());
    }

    [Fact]
    public void Read_MalformedJson_ThrowsRuleParseException()
    {
        Assert.Throws<RuleParseException>(() => _reader.Read("{ not json"));
    }

    [Fact]
    public void Read_CommentsAndTrailingCommas_AreTolerated()
    {
        RuleJsonValue root = _reader.Read("{\n// comment\n\"a\": 1,\n}");
        Assert.True(root.TryGetProperty("a", out _));
    }

    [Fact]
    public void ToClrValue_NumberPolicy_LongThenDecimalThenDouble()
    {
        Assert.IsType<long>(_reader.Read("[9223372036854775807]").Items[0].ToClrValue());
        Assert.IsType<decimal>(_reader.Read("[10.5]").Items[0].ToClrValue());
        Assert.IsType<double>(_reader.Read("[1e300]").Items[0].ToClrValue());
    }
}
