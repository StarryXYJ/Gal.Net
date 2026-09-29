using System.Collections.Frozen;
using System.Collections.ObjectModel;

namespace GalNet.Core.Gallery;

public sealed class GalleryCatalog
{
    private readonly FrozenDictionary<string, GalleryTypeRegistration> _typesById;
    private readonly FrozenDictionary<int, GalleryItem> _itemsById;
    private readonly FrozenDictionary<string, IReadOnlyList<GalleryItem>> _itemsByType;
    private GalleryCatalog(IReadOnlyList<GalleryTypeRegistration> types, IReadOnlyList<GalleryItem> items)
    {
        Types = types; Items = items;
        _typesById = types.ToFrozenDictionary(type => type.TypeId, StringComparer.Ordinal);
        _itemsById = items.ToFrozenDictionary(item => item.Id);
        _itemsByType = types.ToFrozenDictionary(type => type.TypeId, type => (IReadOnlyList<GalleryItem>)new ReadOnlyCollection<GalleryItem>(items.Where(item => item.TypeId == type.TypeId).OrderBy(item => item.Id).ToList()), StringComparer.Ordinal);
    }
    public static GalleryCatalog Empty { get; } = new([], []);
    public IReadOnlyList<GalleryTypeRegistration> Types { get; }
    public IReadOnlyList<GalleryItem> Items { get; }
    public static GalleryCatalog Create(GalleryConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.Version != GalleryConfiguration.CurrentVersion) throw new InvalidDataException($"Unsupported generated gallery.json version '{configuration.Version}'.");
        var types = (configuration.Types ?? throw new InvalidDataException("gallery.json must contain types.")).Select(NormalizeType).OrderBy(type => type.TypeId, StringComparer.Ordinal).ToList();
        if (types.Select(type => type.TypeId).Distinct(StringComparer.Ordinal).Count() != types.Count) throw new InvalidDataException("Gallery types must have unique type IDs.");
        var knownTypes = types.ToDictionary(type => type.TypeId, StringComparer.Ordinal);
        var items = new List<GalleryItem>();
        foreach (var item in configuration.Items ?? throw new InvalidDataException("gallery.json must contain items."))
        {
            if (item.Id <= 0) throw new InvalidDataException("Gallery item IDs must be positive integers.");
            var typeId = NormalizeTypeId(item.TypeId);
            if (!knownTypes.ContainsKey(typeId)) throw new InvalidDataException($"Gallery item '{item.Id}' references unknown type '{typeId}'.");
            if (string.IsNullOrWhiteSpace(item.ResourceId)) throw new InvalidDataException($"Gallery item '{item.Id}' must reference a resource.");
            items.Add(item with { TypeId = typeId, ResourceId = item.ResourceId.Trim(), Title = string.IsNullOrWhiteSpace(item.Title) ? null : item.Title.Trim() });
        }
        if (items.Select(item => item.Id).Distinct().Count() != items.Count) throw new InvalidDataException("Gallery item IDs must be globally unique.");
        return new GalleryCatalog(new ReadOnlyCollection<GalleryTypeRegistration>(types), new ReadOnlyCollection<GalleryItem>(items.OrderBy(item => item.TypeId, StringComparer.Ordinal).ThenBy(item => item.Id).ToList()));
    }
    public bool TryGetType(string typeId, out GalleryTypeRegistration registration) => _typesById.TryGetValue(NormalizeTypeId(typeId), out registration!);
    public bool TryGetItem(int itemId, out GalleryItem item) => _itemsById.TryGetValue(itemId, out item!);
    public IReadOnlyList<GalleryItem> GetItems(string typeId) => _itemsByType.TryGetValue(NormalizeTypeId(typeId), out var items) ? items : throw new KeyNotFoundException($"Gallery type '{typeId}' is not registered.");
    internal static GalleryTypeRegistration NormalizeType(GalleryTypeRegistration source) => source with { TypeId = NormalizeTypeId(source.TypeId), ResourceTypeId = NormalizeTypeId(source.ResourceTypeId) };
    internal static string NormalizeTypeId(string? value)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant();
        if (normalized.Length == 0 || normalized.Any(c => c is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '_' and not '-' and not '.')) throw new InvalidDataException("Type IDs must contain only letters, digits, '_', '-' or '.'.");
        return normalized;
    }
}
