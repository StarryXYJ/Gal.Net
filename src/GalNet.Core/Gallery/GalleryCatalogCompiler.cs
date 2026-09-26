using GalNet.Core.Assets;
namespace GalNet.Core.Gallery;
public sealed class GalleryCatalogCompiler(IResourceTypeCatalog resourceTypes, IGalleryTypeCatalog galleryTypes)
{
    public GalleryCatalog Compile(IEnumerable<AssetMeta> metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var items = new List<GalleryItem>();
        var origins = new Dictionary<int, string>();
        var assetsById = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var meta in metadata.OrderBy(item => item.Path, StringComparer.Ordinal))
        {
            var resource = resourceTypes.Get(meta.TypeId);
            if (!resource.MetaDtoType.IsInstanceOfType(meta)) throw new InvalidDataException($"Asset '{meta.Path}' declares '{meta.TypeId}' but has metadata DTO '{meta.GetType().Name}'.");
            if (!assetsById.TryAdd(meta.Id, meta.Path))
                throw new InvalidDataException($"Asset ID '{meta.Id}' is declared by both '{assetsById[meta.Id]}' and '{meta.Path}'.");
            foreach (var annotation in meta.Gallery ?? [])
            {
                if (annotation.Id <= 0) throw new InvalidDataException($"Gallery annotation in '{meta.Path}' must have a positive ID.");
                var galleryType = galleryTypes.Get(annotation.TypeId);
                if (!string.Equals(galleryType.ResourceTypeId, meta.TypeId, StringComparison.Ordinal)) throw new InvalidDataException($"Gallery type '{galleryType.TypeId}' accepts '{galleryType.ResourceTypeId}', not asset '{meta.Path}' of type '{meta.TypeId}'.");
                if (!origins.TryAdd(annotation.Id, meta.Path)) throw new InvalidDataException($"Gallery ID '{annotation.Id}' is declared by both '{origins[annotation.Id]}' and '{meta.Path}'.");
                items.Add(new GalleryItem { Id = annotation.Id, TypeId = galleryType.TypeId, ResourceId = meta.Id, Title = string.IsNullOrWhiteSpace(annotation.Title) ? null : annotation.Title.Trim() });
            }
        }
        return GalleryCatalog.Create(new GalleryConfiguration { Types = galleryTypes.Types.ToList(), Items = items.ToList() });
    }
}
