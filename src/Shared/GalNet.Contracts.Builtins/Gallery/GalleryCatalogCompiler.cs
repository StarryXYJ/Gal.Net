using GalNet.Core.Assets;
namespace GalNet.Contracts.Builtins.Gallery;

public sealed class GalleryCatalogCompiler(IResourceTypeCatalog resourceTypes, IGalleryTypeCatalog galleryTypes)
{
    public GalleryCatalog Compile(IEnumerable<AssetMeta> metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var items = new List<GalleryItem>();
        var origins = new Dictionary<int, string>();
        var resourceTypesById = new Dictionary<string, string>(StringComparer.Ordinal);
        var assetsById = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var meta in metadata.OrderBy(item => item.Path, StringComparer.Ordinal))
        {
            var resource = resourceTypes.Get(meta.TypeId);
            if (!resource.MetaDtoType.IsInstanceOfType(meta)) throw new InvalidDataException($"Asset '{meta.Path}' declares '{meta.TypeId}' but has metadata DTO '{meta.GetType().Name}'.");
            if (!assetsById.TryAdd(meta.Id, meta.Path))
                throw new InvalidDataException($"Asset ID '{meta.Id}' is declared by both '{assetsById[meta.Id]}' and '{meta.Path}'.");
            resourceTypesById.Add(meta.Id, meta.TypeId);
            if (meta.GalleryUnlockId is not { } id) continue;
            if (id <= 0) throw new InvalidDataException($"Gallery unlock ID in '{meta.Path}' must be positive.");
            if (!origins.TryAdd(id, meta.Path)) throw new InvalidDataException($"Gallery ID '{id}' is declared by both '{origins[id]}' and '{meta.Path}'.");
            var compatible = galleryTypes.Types.Where(type => type.ResourceTypeId == meta.TypeId).ToArray();
            if (compatible.Length == 0) continue;
            if (compatible.Length != 1) throw new InvalidDataException($"Resource type '{meta.TypeId}' has ambiguous Gallery registrations.");
            string? preview = null;
            if (meta.ExtensionData?.TryGetValue("galleryPreviewAssetId", out var image) == true)
            {
                if (image.ValueKind is not (System.Text.Json.JsonValueKind.String or System.Text.Json.JsonValueKind.Null)) throw new InvalidDataException($"Gallery preview in '{meta.Path}' must be a resource ID.");
                preview = image.GetString();
            }
            items.Add(new GalleryItem { Id = id, TypeId = compatible[0].TypeId, ResourceId = meta.Id, DisplayNameKey = meta.DisplayNameKey, PreviewResourceId = string.IsNullOrWhiteSpace(preview) ? null : preview.Trim() });
        }
        foreach (var item in items.Where(item => item.PreviewResourceId is not null))
            if (!resourceTypesById.TryGetValue(item.PreviewResourceId!, out var previewType) || previewType != "sprite")
                throw new InvalidDataException($"Gallery item '{item.Id}' requires an available sprite preview '{item.PreviewResourceId}'.");
        return GalleryCatalog.Create(new GalleryConfiguration { Types = galleryTypes.Types.ToList(), Items = items.ToList() });
    }
}
