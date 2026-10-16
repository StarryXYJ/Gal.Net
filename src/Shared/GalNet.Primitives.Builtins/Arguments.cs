using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Core.Primitives;

namespace GalNet.Primitives.Builtins;

internal static class Arguments
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string RawJson(JsonElement value, string fallback = "{}") =>
        value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? fallback : value.GetRawText();

    public static T? Json<T>(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return default;
        if (value.ValueKind == JsonValueKind.String)
        {
            var json = value.GetString();
            return string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        return value.Deserialize<T>(JsonOptions);
    }
}
