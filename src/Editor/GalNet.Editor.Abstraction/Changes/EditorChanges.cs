using GalNet.Editor.Abstraction.Documents;

namespace GalNet.Editor.Abstraction.Changes;

public interface IEditorChange
{
    IReadOnlyList<string> ChangedResources { get; }
    EditorProjectDocument Apply();
    EditorProjectDocument Revert();
}

/// <summary>
/// Atomic immutable document transition. It is intentionally UI-independent and is safe to store in history.
/// Granular changes can replace this implementation without changing the session contract.
/// </summary>
public sealed class EditorDocumentChange : IEditorChange
{
    private readonly EditorProjectDocument _before;
    private readonly EditorProjectDocument _after;

    public IReadOnlyList<string> ChangedResources { get; }

    public EditorDocumentChange(
        EditorProjectDocument before,
        EditorProjectDocument after,
        IReadOnlyList<string> changedResources)
    {
        _before = EditorDocumentCloner.Clone(before);
        _after = EditorDocumentCloner.Clone(after);
        ChangedResources = changedResources;
    }

    public EditorProjectDocument Apply() => EditorDocumentCloner.Clone(_after);
    public EditorProjectDocument Revert() => EditorDocumentCloner.Clone(_before);
}

public static class EditorDocumentCloner
{
    public static EditorProjectDocument Clone(EditorProjectDocument source) => new()
    {
        Graph = CloneGraph(source.Graph),
        GroupEntries = source.GroupEntries.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Select(CloneEntry).ToList()),
        Settings = CloneSettings(source.Settings)
    };

    public static EditorGraphDocument CloneGraph(EditorGraphDocument source) => new()
    {
        Version = source.Version,
        Name = source.Name,
        RootNodeId = source.RootNodeId,
        Nodes = source.Nodes.Select(CloneNode).ToList(),
        Edges = source.Edges.Select(CloneEdge).ToList(),
        PlayerVariables = source.PlayerVariables.Select(variable => variable.Clone()).ToList(),
        SaveVariables = source.SaveVariables.Select(variable => variable.Clone()).ToList()
    };

    private static EditorGraphNodeDto CloneNode(EditorGraphNodeDto source) => new()
    {
        Id = source.Id,
        Type = source.Type,
        Name = source.Name,
        X = source.X,
        Y = source.Y,
        File = source.File,
        BranchType = source.BranchType,
        Options = source.Options?.Select(option => new EditorGraphBranchOptionDto
        {
            Id = option.Id,
            Text = option.Text,
            Condition = option.Condition
        }).ToList(),
        Conditions = source.Conditions?.Select(condition => new EditorGraphBranchConditionDto
        {
            Id = condition.Id,
            Expression = condition.Expression
        }).ToList()
    };

    private static EditorGraphEdgeDto CloneEdge(EditorGraphEdgeDto source) => new()
    {
        Id = source.Id,
        FromNodeId = source.FromNodeId,
        FromOutlet = source.FromOutlet,
        ToNodeId = source.ToNodeId
    };

    private static EditorEntryData CloneEntry(EditorEntryData source) => new()
    {
        StableId = source.StableId,
        Id = source.Id,
        Type = source.Type,
        Condition = source.Condition,
        Parameters = new Dictionary<string, string>(source.Parameters)
    };

    private static GalNet.Core.Settings.ProjectSettings CloneSettings(GalNet.Core.Settings.ProjectSettings source) => new()
    {
        TargetLocale = new GalNet.Core.I18n.I18nLocale(source.TargetLocale.Code),
        AvailableLocales = source.AvailableLocales.Select(locale => new GalNet.Core.I18n.I18nLocale(locale.Code)).ToList(),
        SaveSlotCount = source.SaveSlotCount,
        SfxChannelCount = source.SfxChannelCount,
        DefaultWidth = source.DefaultWidth,
        DefaultHeight = source.DefaultHeight,
        PlayerVariables = source.PlayerVariables.Select(variable => variable.Clone()).ToList(),
        SaveVariables = source.SaveVariables.Select(variable => variable.Clone()).ToList()
    };

}
