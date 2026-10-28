using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization.Metadata;

namespace GalNet.Core.Assets;

public sealed record ResourceTypeRegistration(string TypeId, Type MetaDtoType, IReadOnlyList<string> Extensions)
{
    public IJsonTypeInfoResolver? JsonResolver { get; init; }
    public Func<AssetMeta>? Create { get; init; }
    public bool HasNoDependencies { get; init; }
}

public interface IResourceTypeCatalog
{
    IReadOnlyList<ResourceTypeRegistration> Types { get; }
    string? DefaultTypeId { get; }
    ResourceTypeRegistration Get(string typeId);
    bool TryGet(string? typeId, out ResourceTypeRegistration registration);
    bool TryGetByExtension(string? extension, out ResourceTypeRegistration registration);
    string NormalizeTypeId(string typeId);
}

public sealed class ResourceTypeCatalogBuilder
{
    private readonly Dictionary<string, (Type Type, IJsonTypeInfoResolver? Resolver, Func<AssetMeta> Create)> _registrations = new(StringComparer.Ordinal);
    private readonly List<(string TypeId, string Extension)> _extensions = [];
    private string? _defaultTypeId;
    private readonly HashSet<string> _leafTypes = new(StringComparer.Ordinal);
    public ResourceTypeCatalogBuilder WithoutDependencies(params string[] typeIds)
    {
        foreach (var id in typeIds) _leafTypes.Add(ResourceTypeCatalog.NormalizeTypeIdCore(id));
        return this;
    }
    public ResourceTypeCatalogBuilder DefaultType(string typeId)
    { _defaultTypeId = ResourceTypeCatalog.NormalizeTypeIdCore(typeId); return this; }

    public ResourceTypeCatalogBuilder Add<TMeta>(string typeId, params string[] extensions) where TMeta : AssetMeta, new() => Add(typeId, typeof(TMeta), false, extensions, null, static () => new TMeta());
    public ResourceTypeCatalogBuilder Replace<TMeta>(string typeId, params string[] extensions) where TMeta : AssetMeta, new() => Add(typeId, typeof(TMeta), true, extensions, null, static () => new TMeta());
    public ResourceTypeCatalogBuilder Add<TMeta>(string typeId, IJsonTypeInfoResolver resolver, params string[] extensions) where TMeta : AssetMeta, new() => Add(typeId, typeof(TMeta), false, extensions, resolver, static () => new TMeta());
    public ResourceTypeCatalogBuilder Replace<TMeta>(string typeId, IJsonTypeInfoResolver resolver, params string[] extensions) where TMeta : AssetMeta, new() => Add(typeId, typeof(TMeta), true, extensions, resolver, static () => new TMeta());
    public ResourceTypeCatalogBuilder AddExtensions(string typeId, params string[] extensions)
    {
        var normalizedType = ResourceTypeCatalog.NormalizeTypeIdCore(typeId);
        foreach (var extension in extensions ?? []) _extensions.Add((normalizedType, ResourceTypeCatalog.NormalizeExtension(extension)));
        return this;
    }

    public IResourceTypeCatalog Build()
    {
        foreach (var id in _leafTypes)
            if (!_registrations.ContainsKey(id)) throw new InvalidOperationException($"Leaf resource type '{id}' is not registered.");
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        var byType = _registrations.Keys.ToDictionary(key => key, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var (typeId, extension) in _extensions)
        {
            if (!byType.TryGetValue(typeId, out var values)) throw new InvalidOperationException($"Resource type '{typeId}' must be fully registered before extensions can be frozen.");
            if (owners.TryGetValue(extension, out var owner) && !StringComparer.Ordinal.Equals(owner, typeId)) throw new InvalidOperationException($"Extension '{extension}' is registered by both '{owner}' and '{typeId}'.");
            owners[extension] = typeId;
            if (!values.Contains(extension, StringComparer.Ordinal)) values.Add(extension);
        }
        if (_defaultTypeId is not null && !_registrations.ContainsKey(_defaultTypeId))
            throw new InvalidOperationException($"Default resource type '{_defaultTypeId}' must be registered.");
        return new ResourceTypeCatalog(_registrations.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
            new ResourceTypeRegistration(pair.Key, pair.Value.Type, new ReadOnlyCollection<string>(byType[pair.Key].OrderBy(item => item, StringComparer.Ordinal).ToList()))
            { JsonResolver = pair.Value.Resolver, Create = pair.Value.Create, HasNoDependencies = _leafTypes.Contains(pair.Key) }).ToList(), _defaultTypeId);
    }

    private ResourceTypeCatalogBuilder Add(string typeId, Type metaType, bool replace, IEnumerable<string> extensions,
        IJsonTypeInfoResolver? resolver, Func<AssetMeta> create)
    {
        var normalized = ResourceTypeCatalog.NormalizeTypeIdCore(typeId);
        if (_registrations.ContainsKey(normalized) && !replace) throw new InvalidOperationException($"Resource type '{normalized}' is already fully registered. Use Replace explicitly.");
        if (replace) _leafTypes.Remove(normalized);
        _registrations[normalized] = (metaType, resolver, create);
        return AddExtensions(normalized, extensions.ToArray());
    }
}

public sealed class ResourceTypeCatalog : IResourceTypeCatalog
{
    private readonly FrozenDictionary<string, ResourceTypeRegistration> _byType;
    private readonly FrozenDictionary<string, ResourceTypeRegistration> _byExtension;
    internal ResourceTypeCatalog(IReadOnlyList<ResourceTypeRegistration> types, string? defaultTypeId)
    {
        DefaultTypeId = defaultTypeId;
        Types = new ReadOnlyCollection<ResourceTypeRegistration>(types.ToList());
        _byType = types.ToFrozenDictionary(type => type.TypeId, StringComparer.Ordinal);
        _byExtension = types.SelectMany(type => type.Extensions.Select(extension => (extension, type))).ToFrozenDictionary(pair => pair.extension, pair => pair.type, StringComparer.Ordinal);
    }
    public IReadOnlyList<ResourceTypeRegistration> Types { get; }
    public string? DefaultTypeId { get; }
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
