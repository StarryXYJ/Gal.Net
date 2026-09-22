using System.Text.Json;
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
    public PrimitiveEntry(string typeId, JsonElement arguments)
    {
        if (string.IsNullOrWhiteSpace(typeId))
            throw new ArgumentException("Primitive type ID is required.", nameof(typeId));
        if (arguments.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Primitive arguments must be a JSON object.", nameof(arguments));

        _typeId = typeId;
        Arguments = arguments.Clone();
    }

    /// <summary>Creates a derived authoring primitive entry.</summary>
    protected PrimitiveEntry()
    {
        Arguments = JsonSerializer.SerializeToElement(new Dictionary<string, object?>());
    }

    /// <summary>Unparsed JSON arguments preserved from compiled content.</summary>
    public JsonElement Arguments { get; private set; }

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

/// <summary>An editor-facing entry that must be expanded into Runtime primitives before execution.</summary>
public abstract class NonPrimitiveEntry : Entry
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
public sealed record EntryDefinition(
    string Type,
    string Category,
    Func<Entry> Factory,
    IReadOnlyDictionary<string, EntryParameterType> Parameters,
    IReadOnlyDictionary<string, string> Defaults,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Options,
    EntryKind Kind,
    PrimitiveDescriptor? Descriptor = null);

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
    NonPrimitive
}
