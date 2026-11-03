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

    /// <summary>The natural entrance to the current group, when available.</summary>
    public GroupEntranceSnapshot? GroupEntrance { get; set; }

    /// <summary>Optional host-owned save data. Execution does not interpret or restore these attachments.</summary>
    public Dictionary<string, System.Text.Json.JsonElement> Attachments { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>A single group entrance without recursively embedding another save.</summary>
public sealed class GroupEntranceSnapshot
{
    public string NodeId { get; init; } = "";
    public Dictionary<string, GalNet.Core.Variable.Variable> Variables { get; init; } = [];
    [System.Text.Json.Serialization.JsonRequired]
    public Dictionary<string, StateSection> States { get; init; } = new(StringComparer.Ordinal);
}
