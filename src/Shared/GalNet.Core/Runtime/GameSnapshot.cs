namespace GalNet.Core.Runtime;

/// <summary>Persisted runtime state needed to resume a game and rebuild its visible scene.</summary>
public sealed class GameSnapshot
{
    public const int CurrentFormatVersion = 5;

    /// <summary>Persisted save format version. Older formats are intentionally rejected.</summary>
    public int Version { get; init; } = CurrentFormatVersion;

    /// <summary>Graph node that should execute after restoration.</summary>
    public string NodeId { get; init; } = "";

    /// <summary>Zero-based position of the next entry within <see cref="NodeId"/>.</summary>
    public int EntryIndex { get; init; }
    public string ContentBuildId { get; init; } = "";
    public string[] StoryPackages { get; init; } = [];

    /// <summary>Save-scoped variable values captured for this snapshot.</summary>
    public Dictionary<string, GalNet.Core.Variable.Variable> Variables { get; init; } = [];

    /// <summary>Layer, control, effect, and transition state used to rehydrate the presentation.</summary>
    [System.Text.Json.Serialization.JsonRequired]
    public Dictionary<string, StateSection> States { get; init; } = new(StringComparer.Ordinal);
}
