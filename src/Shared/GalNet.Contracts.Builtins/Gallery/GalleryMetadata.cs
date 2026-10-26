using System.Text.Json;
using GalNet.Core.Assets;

namespace GalNet.Contracts.Builtins.Gallery;

/// <summary>Optional Gallery annotations over the generic asset metadata envelope.</summary>
public static class GalleryMetadata
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public static IReadOnlyList<GalleryAnnotation> ReadGallery(this AssetMeta metadata)
    {
        if (metadata.ExtensionData is not { } extensions || !extensions.TryGetValue("gallery", out var json)) return [];
        try
        {
            var items = json.Deserialize<GalleryAnnotation[]>(JsonOptions) ?? [];
            if (items.Any(item => item is null)) throw new InvalidDataException($"Invalid Gallery annotation in '{metadata.Path}'.");
            return items;
        }
        catch (JsonException error) { throw new InvalidDataException($"Invalid Gallery annotations in '{metadata.Path}'.", error); }
    }
    public static void SetGallery(this AssetMeta metadata, IEnumerable<GalleryAnnotation> values) =>
        (metadata.ExtensionData ??= new(StringComparer.Ordinal))["gallery"] = JsonSerializer.SerializeToElement(values, JsonOptions);
}
