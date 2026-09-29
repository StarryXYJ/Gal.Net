using System.Collections.ObjectModel;
using GalNet.Core.Assets;
namespace GalNet.Core.Gallery;

public interface IGalleryTypeCatalog { IReadOnlyList<GalleryTypeRegistration> Types { get; } GalleryTypeRegistration Get(string typeId); bool TryGet(string typeId, out GalleryTypeRegistration registration); }
public sealed class GalleryTypeCatalogBuilder
{
    private readonly Dictionary<string, GalleryTypeRegistration> _types = new(StringComparer.Ordinal);
    public GalleryTypeCatalogBuilder Add(string typeId, string resourceTypeId) => AddCore(typeId, resourceTypeId, false);
    public GalleryTypeCatalogBuilder Replace(string typeId, string resourceTypeId) => AddCore(typeId, resourceTypeId, true);
    private GalleryTypeCatalogBuilder AddCore(string typeId, string resourceTypeId, bool replace)
    {
        var type = GalleryCatalog.NormalizeType(new GalleryTypeRegistration { TypeId = typeId, ResourceTypeId = resourceTypeId });
        if (_types.ContainsKey(type.TypeId) && !replace) throw new InvalidOperationException($"Gallery type '{type.TypeId}' is already registered. Use Replace explicitly.");
        _types[type.TypeId] = type; return this;
    }
    public IGalleryTypeCatalog Build(IResourceTypeCatalog resourceTypes)
    {
        foreach (var registration in _types.Values) resourceTypes.Get(registration.ResourceTypeId);
        return new GalleryTypeCatalog(_types.Values.OrderBy(item => item.TypeId, StringComparer.Ordinal).ToList());
    }
}
public sealed class GalleryTypeCatalog(IReadOnlyList<GalleryTypeRegistration> types) : IGalleryTypeCatalog
{
    private readonly IReadOnlyDictionary<string, GalleryTypeRegistration> _byId = types.ToDictionary(item => item.TypeId, StringComparer.Ordinal);
    public IReadOnlyList<GalleryTypeRegistration> Types { get; } = new ReadOnlyCollection<GalleryTypeRegistration>(types.ToList());
    public GalleryTypeRegistration Get(string typeId) => TryGet(typeId, out var result) ? result : throw new KeyNotFoundException($"Gallery type '{typeId}' is not registered.");
    public bool TryGet(string typeId, out GalleryTypeRegistration registration) => _byId.TryGetValue(GalleryCatalog.NormalizeTypeId(typeId), out registration!);
}
