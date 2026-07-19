using System.Text;
using System.Text.Json;
using Rulewright.Serialization;

namespace Rulewright.Sample.BlazorBuilder.Json;

/// <summary>
/// Serializes a <see cref="RuleJsonValue"/> tree back to JSON text. The library-neutral
/// <see cref="RuleJsonValue"/> DOM only has a reader adapter (<c>SystemTextJsonReader</c>) in
/// the shipped packages — there's no writer, because nothing in the engine needs one; loading
/// and evaluating a rule document never requires emitting JSON back out. The builder does, since
/// it edits a document as a tree and must serialize the edited tree back to text for the raw
/// JSON view, validation, and the engine's <c>LoadRuleSet(string)</c> overload.
/// </summary>
public static class RuleJsonValueWriter
{
    public static string ToJsonString(RuleJsonValue value, bool indented = true)
    {
        using var stream = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = indented }))
        {
            Write(value, writer);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void Write(RuleJsonValue value, Utf8JsonWriter writer)
    {
        switch (value.Kind)
        {
            case RuleJsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.Properties)
                {
                    writer.WritePropertyName(property.Key);
                    Write(property.Value, writer);
                }

                writer.WriteEndObject();
                break;

            case RuleJsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.Items)
                {
                    Write(item, writer);
                }

                writer.WriteEndArray();
                break;

            case RuleJsonValueKind.String:
                writer.WriteStringValue(value.GetString());
                break;

            case RuleJsonValueKind.Number:
                writer.WriteRawValue(value.GetRawNumber(), skipInputValidation: true);
                break;

            case RuleJsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;

            case RuleJsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;

            default:
                writer.WriteNullValue();
                break;
        }
    }
}
