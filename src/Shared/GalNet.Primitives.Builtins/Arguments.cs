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

    public static string String(PrimitiveCreateContext context, string name, string fallback = "") =>
        context.Arguments.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    public static float Float(PrimitiveCreateContext context, string name, float fallback = 0) =>
        context.Arguments.TryGetProperty(name, out var value) && value.TryGetSingle(out var result) ? result : fallback;

    public static bool TryFloat(PrimitiveCreateContext context, string name, out float result)
    {
        if (context.Arguments.TryGetProperty(name, out var value) && value.TryGetSingle(out result))
            return true;
        result = default;
        return false;
    }

    public static int Int(PrimitiveCreateContext context, string name, int fallback = 0) =>
        context.Arguments.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : fallback;

    public static bool Bool(PrimitiveCreateContext context, string name, bool fallback = false)
    {
        if (!context.Arguments.TryGetProperty(name, out var value))
            return fallback;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(value.GetString(), out var result) ? result : fallback,
            _ => fallback
        };
    }

    public static TEnum Enum<TEnum>(PrimitiveCreateContext context, string name, TEnum fallback)
        where TEnum : struct, Enum =>
        System.Enum.TryParse<TEnum>(String(context, name), true, out var result) ? result : fallback;

    public static string RawJson(PrimitiveCreateContext context, string name, string fallback = "{}") =>
        context.Arguments.TryGetProperty(name, out var value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? value.GetRawText()
            : fallback;

    public static T? Json<T>(PrimitiveCreateContext context, string name)
    {
        if (!context.Arguments.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return default;
        if (value.ValueKind == JsonValueKind.String)
        {
            var json = value.GetString();
            return string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        return value.Deserialize<T>(JsonOptions);
    }
}
