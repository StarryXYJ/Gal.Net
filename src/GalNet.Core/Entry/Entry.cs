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

/// <summary>A Runtime-ready entry with a corresponding Handler.</summary>
public abstract class PrimitiveEntry : Entry;

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
    EntryKind Kind);

/// <summary>Whether an entry is directly executable or requires compilation first.</summary>
public enum EntryKind
{
    Primitive,
    NonPrimitive
}
