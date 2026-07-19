using Rulewright.Serialization;

namespace Rulewright.Sample.BlazorBuilder.Json;

/// <summary>
/// Parses user-typed text as JSON, falling back to treating it as a JSON string when it isn't
/// valid JSON on its own (e.g. someone typed <c>gold</c> instead of <c>"gold"</c>) — so a value
/// editor never loses an edit just because the user forgot to quote it.
/// </summary>
public static class LenientJsonParser
{
    public static RuleJsonValue ParseOrQuote(string text, IRuleJsonReader reader)
    {
        try
        {
            return reader.Read(text);
        }
        catch (RuleParseException)
        {
            return reader.Read(System.Text.Json.JsonSerializer.Serialize(text));
        }
    }
}
