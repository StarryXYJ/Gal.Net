using System.Collections.ObjectModel;
using System.Text.Json;
using GalNet.Core.Primitives;

namespace GalNet.Core.Entry;

/// <summary>Common instruction identity and owned JSON arguments.</summary>
public abstract class Entry
{
    public int Id { get; set; }
    public string Condition { get; set; } = "";
    public abstract string Type { get; }
    public JsonElement Arguments { get; private set; } = JsonSerializer.SerializeToElement(new Dictionary<string, object?>());
    public void SetArguments(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Entry arguments must be a JSON object.", nameof(arguments));
        Arguments = arguments.Clone();
    }
}

/// <summary>A compiled invocation; authoring entries are distinct types.</summary>
public sealed class PrimitiveEntry : Entry
{
    public PrimitiveEntry(string typeId, JsonElement arguments, string? batchId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeId);
        Type = typeId;
        SetArguments(arguments);
        BatchId = NormalizeBatchId(batchId);
    }
    public override string Type { get; }
    public string? BatchId { get; set; }
    public string StableId { get; set; } = "";
    public string? ReadingBoundaryId { get; set; }
    internal static string? NormalizeBatchId(string? batchId) => string.IsNullOrWhiteSpace(batchId) ? null : batchId;
}

/// <summary>A draft primitive containing author JSON; compilation creates a PrimitiveEntry.</summary>
public sealed class AuthoringPrimitiveEntry(string typeId) : Entry
{
    public override string Type { get; } = !string.IsNullOrWhiteSpace(typeId) ? typeId
        : throw new ArgumentException("Primitive type ID is required.", nameof(typeId));
}

public abstract class CompositeEntry : Entry
{
    public abstract IReadOnlyList<PrimitiveEntry> Compile(EntryCompileContext context);
}

public sealed class EntryCompileContext
{
    public required string SourceEntryId { get; init; }
    public required string Condition { get; init; }
}
/// <summary>Editor-facing input kind for a serialized entry parameter.</summary>
public enum EntryParameterType
{
    Text,
    MultilineText,
    Integer,
    Float,
    Autocomplete,
    ImageAsset,
    AudioAsset,
    VideoAsset,
    EffectProgramAsset,
    Select,
    VariableName,
    Expression,
    Json
}

/// <summary>Schema used to create, validate, and edit one registered entry type.</summary>
/// <remarks>Defaults are copied into each new entry; options constrain editor choices but are not persisted separately.</remarks>
public sealed class EntryDefinition
{
    public EntryDefinition(
        string type,
        string category,
        Func<Entry> factory,
        IReadOnlyDictionary<string, EntryParameterType> parameters,
        IReadOnlyDictionary<string, string> defaults,
        IReadOnlyDictionary<string, IReadOnlyList<string>> options,
        EntryKind kind,
        DynamicParameterTable? dynamicParameters = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(defaults);
        ArgumentNullException.ThrowIfNull(options);

        Type = type;
        Category = category;
        Factory = factory;
        Parameters = Freeze(parameters);
        Defaults = Freeze(defaults);
        Options = new ReadOnlyDictionary<string, IReadOnlyList<string>>(options.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)Array.AsReadOnly(pair.Value.ToArray()),
            StringComparer.Ordinal));
        Kind = kind;
        DynamicParameters = dynamicParameters;
    }

    public string Type { get; }
    public string Category { get; }
    public Func<Entry> Factory { get; }
    public IReadOnlyDictionary<string, EntryParameterType> Parameters { get; }
    public IReadOnlyDictionary<string, string> Defaults { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Options { get; }
    public EntryKind Kind { get; }
    public DynamicParameterTable? DynamicParameters { get; }
    public bool IsReading { get; init; }
    public Type? FieldDataType { get; init; }

    private static ReadOnlyDictionary<TKey, TValue> Freeze<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> values)
        where TKey : notnull => new ReadOnlyDictionary<TKey, TValue>(values.ToDictionary(pair => pair.Key, pair => pair.Value));
}

/// <summary>
/// Composition-supplied authoring catalog. It joins primitive descriptors from a
/// host/module set with Core's non-primitive authoring expansions.
/// </summary>
public interface IEntryCatalog
{
    IReadOnlyList<EntryDefinition> Definitions { get; }
    bool TryGet(string type, out EntryDefinition definition);
    EntryDefinition Get(string type);
    Entry Create(string type, int id = 0, string condition = "", JsonElement? arguments = null);
}

/// <summary>Whether an entry is directly executable or requires compilation first.</summary>
public enum EntryKind
{
    Primitive,
    Composite
}
