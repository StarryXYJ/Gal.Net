using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Shared.Services;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Services;

/// <summary>Object selection only; properties are added from the object row.</summary>
public sealed partial class TimelineObjectEdit : ObservableObject, IDisposable
{
    private readonly TimelineEditSession _owner;
    private readonly SceneTargetCatalog _catalog;
    private bool _disposed;
    internal TimelineDocument Document { get; }
    internal int Generation { get; }
    internal int Frame { get; }
    internal string? SourceHandle { get; }
    public bool IsReplacing => SourceHandle is not null;
    public SceneTargetSelection Selection { get; }
    public SceneTargetSnapshot Snapshot => Selection.Snapshot;
    public IReadOnlyList<ResourcePickerItem> Choices => Snapshot.Choices.Select(choice => choice.Item).ToArray();
    public SceneTarget? Target => Selection.Target;
    [ObservableProperty] private string _message = "";
    public bool IsCurrent => !_disposed && _owner.IsObjectEditCurrent(this) && Selection.IsCurrent;
    internal TimelineObjectEdit(TimelineEditSession owner, SceneTargetCatalog catalog, TimelineDocument document, int generation, int frame, string? source, string group, string entry)
    {
        _owner = owner; _catalog = catalog; Document = document; Generation = generation; Frame = frame; SourceHandle = source;
        Selection = new(catalog, catalog.Query(group, entry, Eligible));
    }
    private bool Eligible(SceneTarget target)
    {
        if (!IsReplacing) return target.Properties.Count > 0;
        var tracks = Document.Tracks.Where(track => track.HandleId == SourceHandle).ToArray();
        return tracks.Length > 0 && tracks.All(track => target.Properties.Any(property => property.Name == track.Property))
            && (target.Handle == SourceHandle || !Document.Tracks.Any(track => track.HandleId == target.Handle && tracks.Any(original => original.Property == track.Property)));
    }
    public Task PrepareAsync() => Selection.PrepareAsync();
    public void Select(string id)
    { if (!IsCurrent || !Selection.Select(id)) Message = _catalog.Text["Scene.Stale"]; }
    public bool Commit()
    {
        if (!IsCurrent || !Selection.TryResolve(out var handle) || Target is null) { Message = _catalog.Text["Scene.Stale"]; return false; }
        if (!IsReplacing) return _owner.SelectObject(this, handle);
        try { return _owner.CommitObjectEdit(this, Document.ReplaceTarget(SourceHandle!, handle), handle); }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or OverflowException)
        { Message = _catalog.Text["Timeline.EditRejected"]; return false; }
    }
    public void Dispose() { if (_disposed) return; _disposed = true; Selection.Dispose(); }
}

public sealed partial class TimelineEditSession
{
    private readonly HashSet<string> _selectedObjects = new(StringComparer.Ordinal);
    private readonly HashSet<(string Handle, string Property)> _pendingProperties = [];
    public IEditorLocalizationService SceneText => _localization;
    public TimelineObjectEdit? ObjectEdit { get; private set; }
    public TimelineObjectEdit? BeginObjectEdit(string? source = null)
    {
        if (_disposed || !CanEdit || _document is null || _target?.EntryId is null || (source is not null && !_document.Tracks.Any(track => track.HandleId == source))) return null;
        // Keep the clicked object row attached while its selector is open.
        Canvas.Cancel(); CancelTimingInput(false); CancelTimingInput(true);
        foreach (var row in Rows) row.Cancel();
        EndObjectEdit();
        ObjectEdit = new(this, _targets, _document, _generation, Frame, source, _target.GroupId, _target.EntryId); OnPropertyChanged(nameof(ObjectEdit)); return ObjectEdit;
    }
    internal bool IsObjectEditCurrent(TimelineObjectEdit draft) => !_disposed && ReferenceEquals(ObjectEdit, draft) && draft.Generation == _generation && ReferenceEquals(draft.Document, _document) && draft.Frame == Frame && _selection.Current == _target;
    internal bool SelectObject(TimelineObjectEdit draft, string handle)
    {
        if (!IsObjectEditCurrent(draft)) return false;
        _selectedObjects.Add(handle); BuildRows(); return true;
    }
    internal bool CommitObjectEdit(TimelineObjectEdit draft, System.Text.Json.JsonElement value, string handle)
    {
        var source = draft.SourceHandle;
        if (!IsObjectEditCurrent(draft) || !Submit(value)) return false;
        _selectedObjects.Remove(source!); _selectedObjects.Add(handle);
        _pendingProperties.RemoveWhere(item => item.Handle == source); RebuildVisibleNodes(); return true;
    }
    public IReadOnlyList<TimelineValueRow> AvailableProperties(string handle) => Rows.Where(row => row.Handle == handle && !row.HasTrack && row.Capability is not null).ToArray();
    public bool AddProperty(TimelineValueRow row)
    {
        if (_disposed || !CanEdit || !Rows.Contains(row) || row.HasTrack || row.Capability is null) return false;
        if (Tree.FirstOrDefault(node => node.ObjectHandle == row.Handle) is { } root) root.IsExpanded = true;
        if (row.DraftValue is not null) return row.ToggleTrack();
        _selectedObjects.Add(row.Handle); _pendingProperties.Add((row.Handle, row.Property));
        RebuildVisibleNodes(); Message = _localization["Timeline.InitialUnknown"]; return true;
    }
    private void PruneObjectDrafts()
    {
        _selectedObjects.RemoveWhere(handle => !_scene.Targets.Any(target => target.Handle == handle));
        _pendingProperties.RemoveWhere(item => !_scene.Targets.Any(target => target.Handle == item.Handle)
            || _document?.Tracks.Any(track => track.HandleId == item.Handle && track.Property == item.Property) == true);
    }
    private void ClearObjectDrafts() { _selectedObjects.Clear(); _pendingProperties.Clear(); }
    public void EndObjectEdit() { var previous = ObjectEdit; ObjectEdit = null; previous?.Dispose(); OnPropertyChanged(nameof(ObjectEdit)); }
}
