using System.Text.Json;
using System.Text.Json.Serialization;

namespace GalNet.Core.Settings;

/// <summary>Stores a JSON document held as text as an embedded JSON value.</summary>
public sealed class EmbeddedJsonStringConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;
        if (reader.TokenType == JsonTokenType.String)
            throw new JsonException("Embedded JSON settings must be a JSON value, not an escaped string.");

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

        using var document = JsonDocument.Parse(value);
        document.RootElement.WriteTo(writer);
    }
}
