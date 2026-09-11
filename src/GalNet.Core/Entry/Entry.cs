namespace GalNet.Core.Entry;

/// <summary>A single executable graph entry. Persisted values intentionally remain strings.</summary>
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
    IReadOnlyDictionary<string, IReadOnlyList<string>> Options);
