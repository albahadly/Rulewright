using Rulewright.Json.SystemText;
using Xunit;

namespace Rulewright.Execution.Tests;

/// <summary>
/// An engine exposes the custom functions registered on it — the per-configuration half of
/// the authoring vocabulary a rule-builder UI needs (the static half is RuleSchemaCatalog).
/// </summary>
public class EngineDiscoveryTests
{
    [Fact]
    public void RegisteredFunctions_ListsNamesSortedOrdinally()
    {
        RulewrightEngine engine = new RulewrightBuilder()
            .UseJsonReader(new SystemTextJsonReader())
            .RegisterFunction("IsWeekend", (f, v) => false)
            .RegisterFunction("AlwaysTrue", (f, v) => true)
            .Build();

        Assert.Equal(new[] { "AlwaysTrue", "IsWeekend" }, engine.RegisteredFunctions.ToArray());
    }

    [Fact]
    public void RegisteredFunctions_EmptyWhenNoneRegistered()
    {
        RulewrightEngine engine = new RulewrightBuilder()
            .UseJsonReader(new SystemTextJsonReader())
            .Build();

        Assert.Empty(engine.RegisteredFunctions);
    }
}
