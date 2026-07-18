using System;
using System.Globalization;
using Rulewright.Json.SystemText;

namespace Rulewright.Execution.Tests;

internal static class TestEngine
{
    internal static readonly RulewrightEngine Engine = new RulewrightBuilder()
        .UseJsonReader(new SystemTextJsonReader())
        .RegisterFunction("AlwaysTrue", (fieldValue, value) => true)
        .RegisterFunction("FieldIsFortyTwo", (fieldValue, value) =>
            fieldValue is int i ? i == 42 : fieldValue is long l && l == 42)
        .RegisterFunction("IsWeekend", (fieldValue, value) => TryDate(fieldValue, out DateTime date)
            && date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        .Build();

    private static bool TryDate(object? value, out DateTime date)
    {
        switch (value)
        {
            case DateTime dt:
                date = dt;
                return true;
            case string s when DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out date):
                return true;
            default:
                date = default;
                return false;
        }
    }

    internal static string WrapRule(string conditionJson)
        => "{\"id\":\"test-rule\",\"condition\":" + conditionJson + "}";

    /// <summary>Loads a single-condition rule and reports whether it fires for the fact.</summary>
    internal static bool Matches<TFact>(string conditionJson, TFact fact)
    {
        LoadedRuleSet loaded = Engine.LoadRuleSet(WrapRule(conditionJson));
        return Engine.Evaluate(loaded, fact).FiredRules.Count == 1;
    }

    internal static OrderFact DefaultFact() => new OrderFact
    {
        Customer = new Customer
        {
            Age = 21,
            IsVip = true,
            Name = "Alice",
            Email = "alice@example.com",
            LoyaltyYears = 3,
            Tier = CustomerTier.Gold,
            JoinedOn = new DateTime(2021, 6, 15, 0, 0, 0, DateTimeKind.Utc),
            Address = new Address { City = "Amsterdam" },
        },
        Order = new Order { Total = 120.50m, Weight = 2.4, Coupon = "SPRING", ItemCount = 3 },
        Tag = "priority",
    };
}
