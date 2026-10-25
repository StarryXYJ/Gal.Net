using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Core.Assets;
using GalNet.Core.Variants;

namespace GalNet.Assets.Variants;

/// <summary>Portable compiled payload. Author directory descriptions are never accepted by the decoder.</summary>
public static class ImageVariantsCodec
{
    public const string ResourceType = "image-variants";
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNameCaseInsensitive = false
    };

    public static ImageVariantsSource ReadSource(ReadOnlyMemory<byte> data)
    {
        RejectDuplicateProperties(data);
        return JsonSerializer.Deserialize<ImageVariantsSource>(data.Span, Options)
            ?? throw new InvalidDataException("Image variants source must be an object.");
    }

    public static byte[] Encode(ImageVariantsDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return JsonSerializer.SerializeToUtf8Bytes(new Payload
        {
            Kind = "image-variants",
            Version = 1,
            Default = definition.Default.Key,
            Dimensions = definition.Dimensions.Select(item => new ImageVariantDimensionSource { Key = item.Key, Label = item.Label }).ToList(),
            Variants = definition.Variants.Select(item => new Variant { Key = item.Key, AssetId = item.AssetId }).ToList()
        }, Options);
    }

    public static ImageVariantsDefinition Decode(ReadOnlyMemory<byte> data)
    {
        RejectDuplicateProperties(data);
        var payload = JsonSerializer.Deserialize<Payload>(data.Span, Options);
        if (payload is not { Kind: "image-variants", Version: 1, Dimensions: not null, Variants: not null })
            throw new InvalidDataException("Unsupported compiled image variants payload.");
        if (payload.Variants.Any(item => item is null || string.IsNullOrEmpty(item.Key)))
            throw new InvalidDataException("Compiled variants require exact keys.");
        // Compiled payloads contain the full inferred structure; only author sources infer missing names.
        if (payload.Dimensions.Count != payload.Variants.Select(item => item.Key.Split('_').Length).DefaultIfEmpty(0).Max())
            throw new InvalidDataException("Compiled dimensions must match the variant positions.");
        var result = ImageVariantsBuilder.Build(new ImageVariantsSource
        {
            Folder = ".",
            Default = payload.Default,
            Dimensions = payload.Dimensions
        }, payload.Variants.Select(item => new ImageVariantFile(item.Key + ".png", item.AssetId)));
        return RequireDefinition(result);
    }

    internal static ImageVariantsDefinition RequireDefinition(ImageVariantsBuildResult result) => result.Definition
        ?? throw new InvalidDataException(string.Join("; ", result.Diagnostics.Select(item => $"{item.Path} [{item.Code}]: {item.Message}")));

    private static void RejectDuplicateProperties(ReadOnlyMemory<byte> data)
    {
        using var document = JsonDocument.Parse(data);
        Check(document.RootElement);
        static void Check(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name)) throw new InvalidDataException($"Duplicate image variants property '{property.Name}'.");
                    Check(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
                foreach (var item in element.EnumerateArray()) Check(item);
        }
    }

    private sealed class Payload
    {
        public string Kind { get; set; } = "";
        public int Version { get; set; }
        public string Default { get; set; } = "";
        public List<ImageVariantDimensionSource>? Dimensions { get; set; }
        public List<Variant>? Variants { get; set; }
    }

    private sealed class Variant
    {
        public string Key { get; set; } = "";
        public string AssetId { get; set; } = "";
    }
}

public sealed class ImageVariantsDecoder : IAssetDecoder<ImageVariantsDefinition>
{
    public ValueTask<ImageVariantsDefinition?> DecodeAsync(IGameFile file, ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (file.TypeId != ImageVariantsCodec.ResourceType) throw new InvalidDataException("Expected an image variants resource.");
        return ValueTask.FromResult<ImageVariantsDefinition?>(ImageVariantsCodec.Decode(data));
    }
}

public static class ImageVariantsAssetManagerExtensions
{
    public static void RegisterImageVariantsDecoder(this IAssetManager assets) =>
        assets.RegisterDecoder<ImageVariantsDefinition>(ImageVariantsCodec.ResourceType, new ImageVariantsDecoder());
}
