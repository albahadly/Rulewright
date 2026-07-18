using System;
using Rulewright.Json.SystemText;
using Rulewright.Serialization;
using Xunit;

namespace Rulewright.Execution.Tests;

/// <summary>
/// Every document under the repository's <c>examples/</c> folder must be a valid Rulewright
/// document that loads cleanly, so the examples cannot drift out of sync with the engine.
/// </summary>
public class ExampleFilesTests
{
    private static readonly RulewrightEngine Engine = new RulewrightBuilder()
        .UseJsonReader(new SystemTextJsonReader())
        .RegisterFunction("IsWeekend", (fieldValue, value) =>
            fieldValue is DateTime date && date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        .Build();

    private static readonly string ExamplesDirectory = FindExamplesDirectory();

    public static IEnumerable<object[]> ExampleFiles()
        => Directory.GetFiles(ExamplesDirectory, "*.json")
            .Select(file => new object[] { Path.GetFileName(file) });

    [Theory]
    [MemberData(nameof(ExampleFiles))]
    public void Example_ValidatesAndLoads(string fileName)
    {
        string json = File.ReadAllText(Path.Combine(ExamplesDirectory, fileName));

        RuleSetValidationResult validation = Engine.Validate(json);
        Assert.True(
            validation.IsValid,
            $"{fileName}: {string.Join("; ", validation.Errors.Select(e => $"{e.Path} {e.Message}"))}");

        // LoadRuleSet parses, validates, resolves custom functions, and prepares the rule set
        // (a decision table expands to rules here).
        LoadedRuleSet loaded = Engine.LoadRuleSet(json);
        Assert.NotEmpty(loaded.RuleSet.Rules);
    }

    private static string FindExamplesDirectory()
    {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Rulewright.slnx"))
                && Directory.Exists(Path.Combine(directory.FullName, "examples")))
            {
                return Path.Combine(directory.FullName, "examples");
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository 'examples' directory.");
    }
}
