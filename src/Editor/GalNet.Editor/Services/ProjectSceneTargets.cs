using System.Text.Json;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Services;

/// <summary>Queries objects immediately before a story position. No UI or runtime execution;
/// one bounded analysis generation is cached per project author snapshot.</summary>
public sealed class ProjectSceneTargets(IEditorDocumentService documents) : IDisposable
{
    private bool _disposed;
    private string? _snapshot;
    private SceneTargetAnalysis? _analysis;
    public long Revision { get; private set; }
    public bool IsAvailable => !_disposed && documents.IsLoaded;
    public SceneTargetResult Before(string groupId, string? entryId)
    {
        if (_disposed) return new([], false, false);
        if (!documents.IsLoaded)
        {
            if (_snapshot is not null) Revision++;
            _snapshot = null; _analysis = null; return new([], false, false);
        }
        var snapshot = JsonSerializer.Serialize(new EditorProjectDocument { Graph = documents.CurrentDocument, GroupEntries = documents.Groups.ToDictionary(pair => pair.Key, pair => pair.Value.Entries) });
        if (snapshot != _snapshot)
        {
            var frozen = JsonSerializer.Deserialize<EditorProjectDocument>(snapshot)!;
            _analysis = new(frozen.Graph, frozen.GroupEntries); _snapshot = snapshot; Revision++;
        }
        return _analysis!.Before(groupId, entryId);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _snapshot = null; _analysis = null; Revision++;
    }
}
