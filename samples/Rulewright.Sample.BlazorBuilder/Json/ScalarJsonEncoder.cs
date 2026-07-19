using System.Globalization;
using System.Text.Json;

namespace Rulewright.Sample.BlazorBuilder.Json;

/// <summary>
/// Turns plain text a user typed into a value editor into a JSON literal, so
/// <see cref="Drafts.LeafDraft.ValueJson"/> always holds valid JSON regardless of what the
/// widget accepted as input.
/// </summary>
public static class ScalarJsonEncoder
{
    /// <summary>
    /// Encodes free text as a JSON scalar, auto-detecting <c>true</c>/<c>false</c>/<c>null</c>
    /// and numbers; anything else becomes a JSON string. Used for operators whose value can be
    /// any scalar (<c>Equals</c>, <c>GreaterThan</c>, a <c>custom</c> function's value, …).
    /// </summary>
    public static string EncodeAuto(string text)
    {
        text = text.Trim();
        if (text.Length == 0 || text == "null")
        {
            return "null";
        }

        if (text is "true" or "false")
        {
            return text;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            return text;
        }

        return JsonSerializer.Serialize(text);
    }

    /// <summary>Encodes text as a JSON string unconditionally — for operators that only take a string.</summary>
    public static string EncodeText(string text) => JsonSerializer.Serialize(text);

    /// <summary>
    /// Encodes a comma-separated list of items as a JSON array, auto-encoding each item with
    /// <see cref="EncodeAuto"/> — for <c>In</c>/<c>NotIn</c>.
    /// </summary>
    public static string EncodeArray(string commaSeparated)
    {
        var items = commaSeparated
            .Split(',')
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Select(EncodeAuto);

        return "[" + string.Join(",", items) + "]";
    }

    /// <summary>The reverse of <see cref="EncodeArray"/>, for re-populating the comma-separated widget from stored JSON.</summary>
    public static string DecodeArrayToCommaSeparated(string valueJson)
    {
        if (string.IsNullOrWhiteSpace(valueJson))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(valueJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return valueJson;
            }

            var items = document.RootElement.EnumerateArray().Select(e => e.ValueKind switch
            {
                JsonValueKind.String => e.GetString() ?? string.Empty,
                _ => e.GetRawText(),
            });

            return string.Join(", ", items);
        }
        catch (JsonException)
        {
            return valueJson;
        }
    }

    /// <summary>The reverse of <see cref="EncodeAuto"/>/<see cref="EncodeText"/>, for populating a scalar widget from stored JSON.</summary>
    public static string DecodeScalarToText(string valueJson)
    {
        if (string.IsNullOrWhiteSpace(valueJson))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(valueJson);
            return document.RootElement.ValueKind == JsonValueKind.String
                ? document.RootElement.GetString() ?? string.Empty
                : document.RootElement.GetRawText();
        }
        catch (JsonException)
        {
            return valueJson;
        }
    }
}
