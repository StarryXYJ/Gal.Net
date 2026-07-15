using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Media;

namespace GalNet.Editor.Shared.UI;

/// <summary>Persists Avalonia colors as explicit ARGB components instead of UI-specific strings.</summary>
internal sealed class ColorJsonConverter : JsonConverter<Color>
{
    public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("A color must be an ARGB object.");

        byte alpha = 0;
        byte red = 0;
        byte green = 0;
        byte blue = 0;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Expected a color component name.");

            var name = reader.GetString();
            reader.Read();
            var value = reader.GetByte();
            switch (name)
            {
                case "A": alpha = value; break;
                case "R": red = value; break;
                case "G": green = value; break;
                case "B": blue = value; break;
            }
        }

        return Color.FromArgb(alpha, red, green, blue);
    }

    public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("A", value.A);
        writer.WriteNumber("R", value.R);
        writer.WriteNumber("G", value.G);
        writer.WriteNumber("B", value.B);
        writer.WriteEndObject();
    }
}
