using System.Collections.Frozen;
using System.Collections.ObjectModel;

namespace GalNet.Core.Assets;

public sealed record ResourceTypeRegistration(string TypeId, Type MetaDtoType, IReadOnlyList<string> Extensions);

public interface IResourceTypeCatalog
{
    IReadOnlyList<ResourceTypeRegistration> Types { get; }
    ResourceTypeRegistration Get(string typeId);
    bool TryGet(string? typeId, out ResourceTypeRegistration registration);
    bool TryGetByExtension(string? extension, out ResourceTypeRegistration registration);
    string NormalizeTypeId(string typeId);
}

public sealed class ResourceTypeCatalogBuilder
{
    private readonly Dictionary<string, Type> _registrations = new(StringComparer.Ordinal);
    private readonly List<(string TypeId, string Extension)> _extensions = [];

    public ResourceTypeCatalogBuilder Add<TMeta>(string typeId, params string[] extensions) where TMeta : AssetMeta => Add(typeId, typeof(TMeta), false, extensions);
    public ResourceTypeCatalogBuilder Replace<TMeta>(string typeId, params string[] extensions) where TMeta : AssetMeta => Add(typeId, typeof(TMeta), true, extensions);
    public ResourceTypeCatalogBuilder AddExtensions(string typeId, params string[] extensions)
    {
        var normalizedType = ResourceTypeCatalog.NormalizeTypeIdCore(typeId);
        foreach (var extension in extensions ?? []) _extensions.Add((normalizedType, ResourceTypeCatalog.NormalizeExtension(extension)));
        return this;
    }

    public IResourceTypeCatalog Build()
    {
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        var byType = _registrations.Keys.ToDictionary(key => key, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var (typeId, extension) in _extensions)
        {
            if (!byType.TryGetValue(typeId, out var values)) throw new InvalidOperationException($"Resource type '{typeId}' must be fully registered before extensions can be frozen.");
            if (owners.TryGetValue(extension, out var owner) && !StringComparer.Ordinal.Equals(owner, typeId)) throw new InvalidOperationException($"Extension '{extension}' is registered by both '{owner}' and '{typeId}'.");
            owners[extension] = typeId;
            if (!values.Contains(extension, StringComparer.Ordinal)) values.Add(extension);
        }
        return new ResourceTypeCatalog(_registrations.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
            new ResourceTypeRegistration(pair.Key, pair.Value, new ReadOnlyCollection<string>(byType[pair.Key].OrderBy(item => item, StringComparer.Ordinal).ToList()))).ToList());
    }

    private ResourceTypeCatalogBuilder Add(string typeId, Type metaType, bool replace, IEnumerable<string> extensions)
    {
        var normalized = ResourceTypeCatalog.NormalizeTypeIdCore(typeId);
        if (!typeof(AssetMeta).IsAssignableFrom(metaType) || metaType.IsAbstract || metaType.GetConstructor(Type.EmptyTypes) is null) throw new ArgumentException($"'{metaType}' must be a concrete AssetMeta DTO with a parameterless constructor.", nameof(metaType));
        if (_registrations.ContainsKey(normalized) && !replace) throw new InvalidOperationException($"Resource type '{normalized}' is already fully registered. Use Replace explicitly.");
        _registrations[normalized] = metaType;
        return AddExtensions(normalized, extensions.ToArray());
    }
}

public sealed class ResourceTypeCatalog : IResourceTypeCatalog
{
    private readonly FrozenDictionary<string, ResourceTypeRegistration> _byType;
    private readonly FrozenDictionary<string, ResourceTypeRegistration> _byExtension;
    internal ResourceTypeCatalog(IReadOnlyList<ResourceTypeRegistration> types)
    {
        Types = new ReadOnlyCollection<ResourceTypeRegistration>(types.ToList());
        _byType = types.ToFrozenDictionary(type => type.TypeId, StringComparer.Ordinal);
        _byExtension = types.SelectMany(type => type.Extensions.Select(extension => (extension, type))).ToFrozenDictionary(pair => pair.extension, pair => pair.type, StringComparer.Ordinal);
    }
    public IReadOnlyList<ResourceTypeRegistration> Types { get; }
    public ResourceTypeRegistration Get(string typeId) => TryGet(typeId, out var result) ? result : throw new KeyNotFoundException($"Resource type '{typeId}' is not registered.");
    public bool TryGet(string? typeId, out ResourceTypeRegistration registration)
    {
        registration = null!;
        return TryNormalizeTypeId(typeId, out var normalized) && _byType.TryGetValue(normalized, out registration!);
    }
    public bool TryGetByExtension(string? extension, out ResourceTypeRegistration registration)
    {
        registration = null!;
        return TryNormalizeExtension(extension, out var normalized) && _byExtension.TryGetValue(normalized, out registration!);
    }
    public string NormalizeTypeId(string typeId) => NormalizeTypeIdCore(typeId);
    internal static string NormalizeTypeIdCore(string? value) => TryNormalizeTypeId(value, out var normalized) ? normalized : throw new ArgumentException("Type IDs must contain only letters, digits, '_', '-' or '.'.", nameof(value));
    internal static string NormalizeExtension(string? value) => TryNormalizeExtension(value, out var normalized) ? normalized : throw new ArgumentException("Extensions must have a leading dot and a non-empty alphanumeric suffix.", nameof(value));
    private static bool TryNormalizeTypeId(string? value, out string normalized) { normalized = (value ?? "").Trim().ToLowerInvariant(); return normalized.Length > 0 && normalized.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-' or '.'); }
    private static bool TryNormalizeExtension(string? value, out string normalized) { normalized = (value ?? "").Trim().ToLowerInvariant(); return normalized.Length > 1 && normalized[0] == '.' && normalized[1..].All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9'); }
}
