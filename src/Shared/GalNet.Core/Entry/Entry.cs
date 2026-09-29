using System.Text.Json;
using System.Collections.ObjectModel;
using GalNet.Core.Primitives;

namespace GalNet.Core.Entry;

/// <summary>A single graph entry. Persisted values intentionally remain strings.</summary>
public abstract class Entry
{
    /// <summary>Identifier assigned by the entry compiler; execution position is tracked separately by the runtime.</summary>
    public int Id { get; set; }

    /// <summary>Expression that must evaluate to <see langword="true"/> before this entry executes.</summary>
    public string Condition { get; set; } = "";

    /// <summary>Serialized parameter values. Concrete entry types interpret the named values.</summary>
    public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
    public abstract string Type { get; }
}

/// <summary>
/// A Runtime-ready primitive invocation. Compiled content always uses this generic
/// shape; concrete authoring entries may temporarily derive from it while being
/// expanded by the compiler.
/// </summary>
public class PrimitiveEntry : Entry
{
    private readonly string? _typeId;

    /// <summary>Creates a generic compiled primitive entry.</summary>
    public PrimitiveEntry(string typeId, JsonElement arguments, string? batchId = null)
    {
        if (string.IsNullOrWhiteSpace(typeId))
            throw new ArgumentException("Primitive type ID is required.", nameof(typeId));
        if (arguments.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Primitive arguments must be a JSON object.", nameof(arguments));

        _typeId = typeId;
        Arguments = arguments.Clone();
        BatchId = NormalizeBatchId(batchId);
    }

    /// <summary>Creates a derived authoring primitive entry.</summary>
    protected PrimitiveEntry()
    {
        Arguments = JsonSerializer.SerializeToElement(new Dictionary<string, object?>());
    }

    /// <summary>Unparsed JSON arguments preserved from compiled content.</summary>
    public JsonElement Arguments { get; private set; }

    /// <summary>Optional skip-batch identity scoped to one execution of the containing group.</summary>
    public string? BatchId { get; set; }

    /// <summary>Whether this is the generic Runtime form rather than an authoring subtype.</summary>
    public bool IsGeneric => _typeId is not null;

    public override string Type => _typeId
        ?? throw new InvalidOperationException("Derived primitive entries must override Type.");

    /// <summary>Replaces arguments when a compiler expands an authoring primitive.</summary>
    public void SetArguments(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Primitive arguments must be a JSON object.", nameof(arguments));
        Arguments = arguments.Clone();
    }

    internal static string? NormalizeBatchId(string? batchId) =>
        string.IsNullOrWhiteSpace(batchId) ? null : batchId;
}

/// <summary>
/// Generic authoring representation of a registered primitive. Concrete primitive
/// schemas live in an outer catalog; Core never needs one CLR type per command.
/// </summary>
public sealed class AuthoringPrimitiveEntry : PrimitiveEntry
{
    private readonly string _typeId;

    public AuthoringPrimitiveEntry(string typeId)
    {
        if (string.IsNullOrWhiteSpace(typeId))
            throw new ArgumentException("Primitive type ID is required.", nameof(typeId));
        _typeId = typeId;
    }

    public override string Type => _typeId;
}

/// <summary>An editor-facing composite entry expanded into Runtime primitives before execution.</summary>
public abstract class CompositeEntry : Entry
{
    /// <summary>Builds the ordered Runtime primitives represented by this entry.</summary>
    public abstract IReadOnlyList<PrimitiveEntry> Compile(EntryCompileContext context);
}

/// <summary>Context available while one source entry is expanded into primitives.</summary>
public sealed class EntryCompileContext
{
    /// <summary>Stable identifier of the source entry being compiled.</summary>
    public required string SourceEntryId { get; init; }

    /// <summary>Condition inherited by every emitted primitive.</summary>
    public required string Condition { get; init; }

    /// <summary>Creates a primitive while preserving the source entry condition.</summary>
    public TPrimitive CreatePrimitive<TPrimitive>() where TPrimitive : PrimitiveEntry, new() =>
        new() { Condition = Condition };
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

    private static IReadOnlyDictionary<TKey, TValue> Freeze<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> values)
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
    Entry Create(string type, int id = 0, string condition = "", IReadOnlyDictionary<string, string>? values = null);
}

/// <summary>Whether an entry is directly executable or requires compilation first.</summary>
public enum EntryKind
{
    Primitive,
    Composite
}
