namespace GalNet.Editor.Services;

/// <summary>Project-owned character drafts. Shared name-text keys never merge character identities.</summary>
public sealed class CharacterPanelState
{
    public string? SelectedNameKey { get; set; }
    public string? SelectedId { get; set; }
    public HashSet<string> AmbiguousNameKeys { get; } = [];
    public Dictionary<(string Id, string NameKey), CharacterPanelDraft> Drafts { get; } = [];
    public CharacterPanelDraft? NewDraft { get; set; }
}

public sealed record CharacterPanelDraft(CharacterRow? Baseline, string Name, string Key);
