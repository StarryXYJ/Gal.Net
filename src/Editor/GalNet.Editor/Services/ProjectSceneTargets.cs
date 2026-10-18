using System.Text.Json;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Services;

/// <summary>One bounded analysis generation per author snapshot, independent of page ownership.</summary>
public sealed class ProjectSceneTargets(IEditorDocumentService documents)
{
    private string? _snapshot;
    private SceneTargetAnalysis? _analysis;
    public SceneTargetResult Before(string groupId, string entryId)
    {
        if (!documents.IsLoaded) { _snapshot = null; _analysis = null; return new([], false, false); }
        var snapshot = JsonSerializer.Serialize(new EditorProjectDocument { Graph = documents.CurrentDocument, GroupEntries = documents.Groups.ToDictionary(pair => pair.Key, pair => pair.Value.Entries) });
        if (snapshot != _snapshot)
        {
            var frozen = JsonSerializer.Deserialize<EditorProjectDocument>(snapshot)!;
            _analysis = new(frozen.Graph, frozen.GroupEntries); _snapshot = snapshot;
        }
        return _analysis!.Before(groupId, entryId);
    }
}
