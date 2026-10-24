using GalNet.Editor.Dock;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Services;

public sealed record AuthorNavigationTarget(string? NodeId = null, string? EntryId = null, string? FieldId = null, string? File = null, long? Revision = null);

/// <summary>Resolves stable author identities at navigation time; never follows an old row index.</summary>
public sealed class AuthorNavigationService(EditorWorkspaceViewModel workspace, EditorDockFactory dock, ProjectTextFiles files)
{
    public event Action<AuthorNavigationTarget>? Requested;
    public bool Navigate(AuthorNavigationTarget target)
    {
        if (target.NodeId is { } nodeId)
        {
            var node = workspace.Nodes.FirstOrDefault(item => item.Id == nodeId);
            if (node is null || target.EntryId is { } entryId && !node.Entries.Any(item => item.StableId == entryId)) return false;
            dock.NavigateLocalization(nodeId, target.EntryId);
            dock.ShowInspector();
        }
        else if (target.File is { } path)
        {
            try { var file = files.Open(path); dock.OpenFileEditor(file); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or GalNet.Editor.Abstraction.Services.EditorOperationException)
            { return false; }
        }
        else return false;
        Requested?.Invoke(target); return true;
    }
}
