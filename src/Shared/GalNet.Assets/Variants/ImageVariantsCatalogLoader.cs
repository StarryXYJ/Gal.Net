using GalNet.Core.Assets;
using GalNet.Contracts.Builtins.Variants;

namespace GalNet.Assets.Variants;

public static class ImageVariantsCatalogLoader
{
    /// <summary>Prepare definitions and verify all sprite references before constructing execution modules.</summary>
    public static async Task<ImageVariantsCatalog> LoadImageVariantsCatalogAsync(this IAssetManager assets, CancellationToken ct = default)
    {
        var files = await assets.GetFilesAsync(ct: ct).ConfigureAwait(false);
        var byId = files.ToDictionary(file => file.Id, StringComparer.OrdinalIgnoreCase);
        var definitions = new Dictionary<string, ImageVariantsDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files.Where(file => file.TypeId == ImageVariantsCodec.ResourceType))
        {
            ct.ThrowIfCancellationRequested();
            using var handle = await assets.AcquireAsync<ImageVariantsDefinition>(file.Id, ct).ConfigureAwait(false)
                ?? throw new InvalidDataException($"Cannot decode image variants resource '{file.Id}'.");
            foreach (var variant in handle.Value.Variants)
                if (!byId.TryGetValue(variant.AssetId, out var sprite) || sprite.TypeId != "sprite")
                    throw new InvalidDataException($"Image variants '{file.Id}/{variant.Key}' references missing or non-sprite '{variant.AssetId}'.");
            definitions.Add(file.Id, handle.Value);
        }
        ct.ThrowIfCancellationRequested();
        return new ImageVariantsCatalog(definitions);
    }
}
