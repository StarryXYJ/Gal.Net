namespace GalNet.Core.Entry;

/// <summary>A single executable graph entry. Persisted values intentionally remain strings.</summary>
public abstract class Entry
{
    public int Id { get; set; }
    public string Condition { get; set; } = "";
    public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
    public abstract string Type { get; }
}

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
    Select
}

public sealed record EntryDefinition(
    string Type,
    Func<Entry> Factory,
    IReadOnlyDictionary<string, EntryParameterType> Parameters,
    IReadOnlyDictionary<string, string> Defaults,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Options);
