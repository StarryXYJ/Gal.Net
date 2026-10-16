using System.Text.Json;
using System.Text.Json.Serialization;

namespace GalNet.Core.Parameters;

/// <summary>String wire values with a distinct CLR type; choices also drive validation and editor descriptions.</summary>
public interface IStringValueEncoding
{
    IReadOnlyList<string> Choices { get; }
}

public sealed class StringBooleanValueConverter : JsonConverter<bool>, IStringValueEncoding
{
    public IReadOnlyList<string> Choices { get; } = Array.AsReadOnly(new[] { "true", "false" });
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String ? reader.GetString() switch
        {
            "true" => true,
            "false" => false,
            _ => throw new JsonException("Expected true or false encoded as a string.")
        } : throw new JsonException("Expected a string boolean.");
    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) => writer.WriteStringValue(value ? "true" : "false");
}

public class StringEnumValueConverter<T> : JsonConverter<T>, IStringValueEncoding where T : struct, Enum
{
    private readonly Dictionary<string, T> _values;
    private readonly Dictionary<T, string> _names;
    public StringEnumValueConverter() : this(null) { }
    protected StringEnumValueConverter(JsonNamingPolicy? policy)
    {
        var pairs = Enum.GetNames<T>().Select(name => new KeyValuePair<string, T>(policy?.ConvertName(name) ?? name, Enum.Parse<T>(name))).ToArray();
        _values = pairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        _names = pairs.GroupBy(pair => pair.Value).ToDictionary(group => group.Key, group => group.First().Key);
        Choices = Array.AsReadOnly(pairs.Select(pair => pair.Key).ToArray());
    }
    public IReadOnlyList<string> Choices { get; }
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String && _values.TryGetValue(reader.GetString()!, out var value)
            ? value : throw new JsonException($"Expected a named {typeof(T).Name} string.");
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteStringValue(_names.TryGetValue(value, out var name) ? name : throw new JsonException($"Undefined {typeof(T).Name} value."));
}
