using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GalNet.Core.Settings;

/// <summary>
/// Writes a JSON document held in a string as a nested JSON value. It also reads
/// the legacy escaped-string representation so existing editor settings remain valid.
/// </summary>
public sealed class RawJsonStringConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;
        if (reader.TokenType == JsonTokenType.String)
            return reader.GetString();

        using var document = JsonDocument.ParseValue(ref reader);
        return document.RootElement.GetRawText();
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            writer.WriteNullValue();
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            document.RootElement.WriteTo(writer);
        }
        catch (JsonException)
        {
            // Preserve a manually edited, invalid value rather than preventing all
            // editor settings from being saved.
            writer.WriteStringValue(value);
        }
    }
}
