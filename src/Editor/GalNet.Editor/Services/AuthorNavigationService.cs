using GalNet.Editor.Dock;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Services.Interfaces;

namespace GalNet.Editor.Services;

public sealed record AuthorNavigationTarget(string? NodeId = null, string? EntryId = null, string? FieldId = null, string? File = null, long? Revision = null,
    IReadOnlyList<string>? FieldPath = null);

/// <summary>Resolves stable author identities at navigation time; never follows an old row index.</summary>
public sealed class AuthorNavigationService(EditorWorkspaceViewModel workspace, EditorDockFactory dock, ProjectTextFiles files, IEditorWindowFactory? windows = null)
{
    public event Action<AuthorNavigationTarget>? Requested;
    public bool CanNavigate(AuthorNavigationTarget target)
    {
        if (target.NodeId is { } nodeId)
        {
            var node = workspace.Nodes.FirstOrDefault(item => item.Id == nodeId);
            return node is not null && (target.EntryId is null || node.Entries.Any(item => item.StableId == target.EntryId));
        }
        return target.File is { } path && files.CanOpen(path);
    }
    public bool Navigate(AuthorNavigationTarget target)
    {
        if (!CanNavigate(target)) return false;
        if (target.NodeId is { } nodeId)
        {
            var node = workspace.Nodes.FirstOrDefault(item => item.Id == nodeId);
            if (node is null || target.EntryId is { } entryId && !node.Entries.Any(item => item.StableId == entryId)) return false;
            var entry = node.Entries.FirstOrDefault(item => item.StableId == target.EntryId);
            if (entry is not null) entry.IsParametersExpanded = true;
            dock.NavigateLocalization(nodeId, target.EntryId);
            dock.ShowInspector();
        }
        else if (target.File is { } path)
        {
            if (path.Replace('\\', '/') == "Graph/graph.json") { dock.OpenPanel(EditorDockPanelIds.NodeGraph); Requested?.Invoke(target); return true; }
            if (path == "settings.json" && windows is not null) { windows.CreateProjectSettingsWindow().Show(); Requested?.Invoke(target); return true; }
            try { var file = files.Open(path); dock.OpenFileEditor(file); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or GalNet.Editor.Abstraction.Services.EditorOperationException)
            { return false; }
        }
        else return false;
        Requested?.Invoke(target); return true;
    }
}
