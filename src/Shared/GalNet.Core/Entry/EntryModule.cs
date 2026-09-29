using System.Collections.ObjectModel;
using System.Text.Json;
using GalNet.Core.Primitives;

namespace GalNet.Core.Entry;

/// <summary>Immutable authoring and runtime metadata owned by one entry module.</summary>
public abstract class EntryBase
{
    protected EntryBase(string name, DynamicParameterTable parameters, string? category = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(parameters);
        Name = name;
        Parameters = parameters;
        Category = string.IsNullOrWhiteSpace(category) ? GetDefaultCategory(name) : category;
    }

    public string Name { get; }
    public string Category { get; }
    public DynamicParameterTable Parameters { get; }

    internal abstract EntryDefinition CreateDefinition();

    protected EntryDefinition BuildDefinition(
        Func<Entry> factory,
        EntryKind kind)
    {
        var editorParameters = new Dictionary<string, EntryParameterType>(StringComparer.Ordinal);
        var defaults = new Dictionary<string, string>(StringComparer.Ordinal);
        var options = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var parameter in Parameters.Values)
        {
            editorParameters.Add(parameter.Name, EntrySchema.GetEditorType(parameter));
            if (parameter.DefaultValue is { } defaultValue)
                defaults.Add(parameter.Name, ToPersistedValue(defaultValue));
            if (parameter.TryGetConstraint("options", out var values) && values.ValueKind == JsonValueKind.Array)
                options.Add(parameter.Name, values.EnumerateArray().Select(value => value.GetString() ?? "").ToArray());
            else if (parameter.ValueType == typeof(bool))
                options.Add(parameter.Name, ["true", "false"]);
        }

        return new EntryDefinition(
            Name,
            Category,
            factory,
            editorParameters,
            defaults,
            options,
            kind,
            Parameters);
    }

    private static string GetDefaultCategory(string name)
    {
        var separator = name.IndexOf('.');
        return separator > 0 ? name[..separator] : name;
    }

    private static string ToPersistedValue(JsonElement value) => value.ValueKind == JsonValueKind.String
        ? value.GetString() ?? ""
        : value.GetRawText();
}

/// <summary>A Runtime-ready primitive definition with a per-invocation instance factory.</summary>
public abstract class PrimitiveEntryBase : EntryBase
{
    protected PrimitiveEntryBase(
        string name,
        DynamicParameterTable parameters,
        string? category = null)
        : base(name, parameters, category)
    { }

    public abstract PrimitiveInstance CreateInstance(PrimitiveCreateContext context);

    internal override EntryDefinition CreateDefinition() => BuildDefinition(
        () => new AuthoringPrimitiveEntry(Name),
        EntryKind.Primitive);
}

/// <summary>Default primitive definition backed directly by a factory delegate.</summary>
public sealed class DefaultPrimitiveEntryBase : PrimitiveEntryBase
{
    private readonly Func<PrimitiveCreateContext, PrimitiveInstance> _factory;

    public DefaultPrimitiveEntryBase(
        string name,
        DynamicParameterTable parameters,
        Func<PrimitiveCreateContext, PrimitiveInstance> factory,
        string? category = null)
        : base(name, parameters, category)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    public override PrimitiveInstance CreateInstance(PrimitiveCreateContext context) =>
        _factory(context) ?? throw new InvalidOperationException($"Primitive '{Name}' returned no runtime instance.");
}

/// <summary>An editor/compiler-only entry that expands into one or more primitives.</summary>
public abstract class CompositeEntryBase : EntryBase
{
    protected CompositeEntryBase(string name, DynamicParameterTable parameters, string? category = null)
        : base(name, parameters, category) { }

    public abstract CompositeEntry CreateEntry();

    internal override EntryDefinition CreateDefinition() => BuildDefinition(
        () => CreateEntry(),
        EntryKind.Composite);
}

/// <summary>Default composite definition backed by an authoring-entry factory.</summary>
public sealed class DefaultCompositeEntryBase : CompositeEntryBase
{
    private readonly Func<CompositeEntry> _factory;

    public DefaultCompositeEntryBase(
        string name,
        DynamicParameterTable parameters,
        Func<CompositeEntry> factory,
        string? category = null)
        : base(name, parameters, category)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    public override CompositeEntry CreateEntry() =>
        _factory() ?? throw new InvalidOperationException($"Composite entry '{Name}' returned no authoring entry.");
}

/// <summary>A module owns independent frozen primitive and composite entry tables.</summary>
public interface IEntryModule : IDisposable
{
    string Id { get; }
    IReadOnlyDictionary<string, PrimitiveEntryBase> PrimitiveEntries { get; }
    IReadOnlyDictionary<string, CompositeEntryBase> CompositeEntries { get; }

    void IDisposable.Dispose() { }
}

/// <summary>Core helper that validates and freezes a module's two entry tables.</summary>
public abstract class EntryModuleBase : IEntryModule
{
    protected EntryModuleBase(
        string id,
        IEnumerable<PrimitiveEntryBase>? primitiveEntries = null,
        IEnumerable<CompositeEntryBase>? compositeEntries = null)
    {
        ValidateModuleId(id);
        Id = id;
        PrimitiveEntries = Freeze(primitiveEntries ?? [], nameof(primitiveEntries));
        CompositeEntries = Freeze(compositeEntries ?? [], nameof(compositeEntries));

        var collision = PrimitiveEntries.Keys.FirstOrDefault(CompositeEntries.ContainsKey);
        if (collision is not null)
            throw new ArgumentException($"Entry '{collision}' is declared as both primitive and composite.");
    }

    public string Id { get; }
    public IReadOnlyDictionary<string, PrimitiveEntryBase> PrimitiveEntries { get; }
    public IReadOnlyDictionary<string, CompositeEntryBase> CompositeEntries { get; }

    public virtual void Dispose() { }

    private static IReadOnlyDictionary<string, TEntry> Freeze<TEntry>(IEnumerable<TEntry> entries, string argumentName)
        where TEntry : EntryBase
    {
        var result = new Dictionary<string, TEntry>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (!result.TryAdd(entry.Name, entry))
                throw new ArgumentException($"Entry '{entry.Name}' is declared more than once.", argumentName);
        }
        return new ReadOnlyDictionary<string, TEntry>(result);
    }

    private static void ValidateModuleId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Contains('.') ||
            !string.Equals(id, id.ToLowerInvariant(), StringComparison.Ordinal))
            throw new ArgumentException("Entry module IDs must be non-empty, lowercase, dot-free identifiers.", nameof(id));
    }
}

/// <summary>Default immutable module for composition roots that need no custom lifecycle.</summary>
public sealed class DefaultEntryModule(
    string id,
    IEnumerable<PrimitiveEntryBase>? primitiveEntries = null,
    IEnumerable<CompositeEntryBase>? compositeEntries = null)
    : EntryModuleBase(id, primitiveEntries, compositeEntries);
