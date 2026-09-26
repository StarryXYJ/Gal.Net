using System.Collections.Frozen;
using System.Collections.ObjectModel;

namespace GalNet.Core.Gallery;

/// <summary>Validated, immutable Gallery type and item index.</summary>
public sealed class GalleryCatalog
{
    private readonly FrozenDictionary<string, GalleryTypeRegistration> _typesById;
    private readonly FrozenDictionary<string, GalleryItem> _itemsById;
    private readonly FrozenDictionary<string, IReadOnlyList<GalleryItem>> _itemsByType;

    private GalleryCatalog(
        IReadOnlyList<GalleryTypeRegistration> types,
        IReadOnlyList<GalleryItem> items,
        FrozenDictionary<string, GalleryTypeRegistration> typesById,
        FrozenDictionary<string, GalleryItem> itemsById,
        FrozenDictionary<string, IReadOnlyList<GalleryItem>> itemsByType)
    {
        Types = types;
        Items = items;
        _typesById = typesById;
        _itemsById = itemsById;
        _itemsByType = itemsByType;
    }

    public static GalleryCatalog Empty { get; } = Create(new GalleryConfiguration());

    public IReadOnlyList<GalleryTypeRegistration> Types { get; }

    public IReadOnlyList<GalleryItem> Items { get; }

    public static GalleryCatalog Create(GalleryConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.Version != GalleryConfiguration.CurrentVersion)
            throw new InvalidDataException($"Unsupported gallery.json version '{configuration.Version}'.");

        var sourceTypes = configuration.Types
            ?? throw new InvalidDataException("gallery.json must contain a types array.");
        var sourceItems = configuration.Items
            ?? throw new InvalidDataException("gallery.json must contain an items array.");

        var types = new List<GalleryTypeRegistration>(sourceTypes.Count);
        var typesById = new Dictionary<string, GalleryTypeRegistration>(StringComparer.Ordinal);
        foreach (var source in sourceTypes)
        {
            if (source is null)
                throw new InvalidDataException("Gallery types cannot contain null entries.");

            var typeId = NormalizeTypeId(source.TypeId);
            var resourceTypeName = NormalizeResourceTypeName(source.ResourceTypeName);
            var registration = source with { TypeId = typeId, ResourceTypeName = resourceTypeName };
            if (!typesById.TryAdd(typeId, registration))
                throw new InvalidDataException($"Gallery type '{typeId}' is registered more than once.");
            types.Add(registration);
        }

        var items = new List<GalleryItem>(sourceItems.Count);
        var itemsById = new Dictionary<string, GalleryItem>(StringComparer.Ordinal);
        var itemsByType = types.ToDictionary(
            type => type.TypeId,
            _ => new List<GalleryItem>(),
            StringComparer.Ordinal);

        foreach (var source in sourceItems)
        {
            if (source is null)
                throw new InvalidDataException("Gallery items cannot contain null entries.");

            var itemId = NormalizeItemId(source.Id);
            var typeId = NormalizeTypeId(source.TypeId);
            if (!typesById.ContainsKey(typeId))
                throw new InvalidDataException($"Gallery item '{itemId}' references unknown type '{typeId}'.");
            if (string.IsNullOrWhiteSpace(source.ResourceId))
                throw new InvalidDataException($"Gallery item '{itemId}' must reference a resource.");

            var item = source with
            {
                Id = itemId,
                TypeId = typeId,
                ResourceId = source.ResourceId.Trim(),
                Title = string.IsNullOrWhiteSpace(source.Title) ? null : source.Title.Trim()
            };
            if (!itemsById.TryAdd(itemId, item))
                throw new InvalidDataException($"Gallery item '{itemId}' is declared more than once.");

            items.Add(item);
            itemsByType[typeId].Add(item);
        }

        var frozenItemsByType = itemsByType.ToFrozenDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<GalleryItem>)new ReadOnlyCollection<GalleryItem>(pair.Value),
            StringComparer.Ordinal);

        return new GalleryCatalog(
            new ReadOnlyCollection<GalleryTypeRegistration>(types),
            new ReadOnlyCollection<GalleryItem>(items),
            typesById.ToFrozenDictionary(StringComparer.Ordinal),
            itemsById.ToFrozenDictionary(StringComparer.Ordinal),
            frozenItemsByType);
    }

    public bool TryGetType(string typeId, out GalleryTypeRegistration registration)
    {
        if (!TryNormalizeTypeId(typeId, out var normalized))
        {
            registration = null!;
            return false;
        }

        return _typesById.TryGetValue(normalized, out registration!);
    }

    public bool TryGetItem(string itemId, out GalleryItem item)
    {
        if (!TryNormalizeItemId(itemId, out var normalized))
        {
            item = null!;
            return false;
        }

        return _itemsById.TryGetValue(normalized, out item!);
    }

    public IReadOnlyList<GalleryItem> GetItems(string typeId)
    {
        var normalized = NormalizeTypeId(typeId);
        if (!_itemsByType.TryGetValue(normalized, out var items))
            throw new KeyNotFoundException($"Gallery type '{normalized}' is not registered.");
        return items;
    }

    private static string NormalizeTypeId(string? value)
    {
        if (!TryNormalizeTypeId(value, out var normalized))
            throw new InvalidDataException("Gallery type IDs must contain only letters, digits, '_', '-' or '.'.");
        return normalized;
    }

    private static bool TryNormalizeTypeId(string? value, out string normalized) =>
        TryNormalize(value, allowDashAndDot: true, out normalized);

    private static string NormalizeItemId(string? value)
    {
        if (!TryNormalizeItemId(value, out var normalized))
            throw new InvalidDataException("Gallery item IDs must contain only letters, digits or '_'.");
        return normalized;
    }

    private static bool TryNormalizeItemId(string? value, out string normalized) =>
        TryNormalize(value, allowDashAndDot: false, out normalized);

    private static string NormalizeResourceTypeName(string? value)
    {
        if (!TryNormalize(value, allowDashAndDot: true, out var normalized))
            throw new InvalidDataException("Gallery resource type names must contain only letters, digits, '_', '-' or '.'.");
        return normalized;
    }

    private static bool TryNormalize(string? value, bool allowDashAndDot, out string normalized)
    {
        normalized = (value ?? "").Trim().ToLowerInvariant();
        if (normalized.Length == 0)
            return false;

        foreach (var character in normalized)
        {
            if (character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_')
                continue;
            if (allowDashAndDot && character is '-' or '.')
                continue;
            return false;
        }

        return true;
    }
}
