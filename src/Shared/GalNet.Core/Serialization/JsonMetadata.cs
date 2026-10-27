using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace GalNet.Core.Serialization;

/// <summary>Composes generated metadata per options instance; reflection is only a desktop fallback.</summary>
public static class JsonMetadata
{
    private static readonly ConditionalWeakTable<JsonSerializerOptions, JsonSerializerOptions> Prepared = new();
    private static readonly JsonSerializerOptions DefaultOptions = CreateOptions();

    public static JsonSerializerOptions Prepare(JsonSerializerOptions? options = null) =>
        options is null ? DefaultOptions : Prepared.GetValue(options, value => CreateOptions(value));

    public static JsonSerializerOptions CreateOptions(JsonSerializerOptions? template = null, IJsonTypeInfoResolver? resolver = null,
        bool stringEnums = false)
    {
        var options = template is null ? new JsonSerializerOptions() : new(template);
        var resolvers = new List<IJsonTypeInfoResolver>();
        if (resolver is not null) resolvers.Add(resolver);
        if (template?.TypeInfoResolver is { } existing) resolvers.Add(existing);
        resolvers.Add(CoreJsonContext.Default);
        if (JsonSerializer.IsReflectionEnabledByDefault) resolvers.Add(ReflectionResolver());
        if (stringEnums && RuntimeFeature.IsDynamicCodeSupported) options.Converters.Add(ReflectionEnums());
        options.TypeInfoResolver = JsonTypeInfoResolver.Combine(resolvers.ToArray());
        options.MakeReadOnly();
        Prepared.Add(options, options);
        return options;
    }

    public static JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions? options = null)
    {
        try { return Prepare(options).GetTypeInfo(type); }
        catch (NotSupportedException error)
        {
            throw new NotSupportedException($"No JSON metadata is registered for '{type}'. Supply a generated JsonSerializerContext at the contract registration boundary.", error);
        }
    }

    public static JsonTypeInfo<T> GetTypeInfo<T>(JsonSerializerOptions? options = null) => (JsonTypeInfo<T>)GetTypeInfo(typeof(T), options);

    [RequiresUnreferencedCode("Reflection fallback is only enabled when JsonSerializer.IsReflectionEnabledByDefault is true.")]
    private static IJsonTypeInfoResolver ReflectionResolver() => new DefaultJsonTypeInfoResolver();

    [RequiresDynamicCode("Generated contexts provide enum metadata when dynamic code is unavailable.")]
    private static JsonConverter ReflectionEnums() => new JsonStringEnumConverter();
}

/// <summary>JSON operations always select explicit metadata, including when reflection is disabled.</summary>
public static class JsonCodec
{
    public static JsonElement SerializeToElement<T>(T value, JsonSerializerOptions? options = null) =>
        JsonSerializer.SerializeToElement(value, JsonMetadata.GetTypeInfo<T>(options));
    public static JsonElement SerializeToElement(object? value, Type type, JsonSerializerOptions? options = null) =>
        JsonSerializer.SerializeToElement(value, JsonMetadata.GetTypeInfo(type, options));
    public static string Serialize<T>(T value, JsonSerializerOptions? options = null) =>
        JsonSerializer.Serialize(value, JsonMetadata.GetTypeInfo<T>(options));
    public static string Serialize(object? value, Type type, JsonSerializerOptions? options = null) =>
        JsonSerializer.Serialize(value, JsonMetadata.GetTypeInfo(type, options));
    public static void Serialize<T>(Utf8JsonWriter writer, T value, JsonSerializerOptions? options = null) =>
        JsonSerializer.Serialize(writer, value, JsonMetadata.GetTypeInfo<T>(options));
    public static byte[] SerializeToUtf8Bytes<T>(T value, JsonSerializerOptions? options = null) =>
        JsonSerializer.SerializeToUtf8Bytes(value, JsonMetadata.GetTypeInfo<T>(options));
    public static T? Deserialize<T>(string json, JsonSerializerOptions? options = null) =>
        JsonSerializer.Deserialize(json, JsonMetadata.GetTypeInfo<T>(options));
    public static T? Deserialize<T>(ReadOnlySpan<byte> json, JsonSerializerOptions? options = null) =>
        JsonSerializer.Deserialize(json, JsonMetadata.GetTypeInfo<T>(options));
    public static T? Deserialize<T>(JsonElement json, JsonSerializerOptions? options = null) =>
        JsonSerializer.Deserialize(json, JsonMetadata.GetTypeInfo<T>(options));
    public static T? Deserialize<T>(ref Utf8JsonReader reader, JsonSerializerOptions? options = null) =>
        JsonSerializer.Deserialize(ref reader, JsonMetadata.GetTypeInfo<T>(options));
    public static object? Deserialize(JsonElement json, Type type, JsonSerializerOptions? options = null) =>
        JsonSerializer.Deserialize(json, JsonMetadata.GetTypeInfo(type, options));
    public static object? Deserialize(string json, Type type, JsonSerializerOptions? options = null) =>
        JsonSerializer.Deserialize(json, JsonMetadata.GetTypeInfo(type, options));
    public static ValueTask<T?> DeserializeAsync<T>(Stream stream, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default) =>
        JsonSerializer.DeserializeAsync(stream, JsonMetadata.GetTypeInfo<T>(options), cancellationToken);
}
