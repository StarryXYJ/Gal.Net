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
    public SceneTargetSelection Selection { get; }
    public SceneTargetSnapshot Snapshot => Selection.Snapshot;
    public IReadOnlyList<ResourcePickerItem> Choices => Snapshot.Choices.Select(choice => choice.Item).ToArray();
    public SceneTarget? Target => Selection.Target;
    [ObservableProperty] private string _message = "";
    public bool IsCurrent => !_disposed && _owner.IsObjectEditCurrent(this) && Selection.IsCurrent;
    internal TimelineObjectEdit(TimelineEditSession owner, SceneTargetCatalog catalog, TimelineDocument document, int generation, int frame, string group, string entry)
    {
        _owner = owner; _catalog = catalog; Document = document; Generation = generation; Frame = frame;
        Selection = new(catalog, catalog.Query(group, entry, Eligible, new(frame, IncludeFuture: true)));
    }
    private static bool Eligible(SceneTarget target) => target.Properties.Count > 0;
    public Task PrepareAsync() => Selection.PrepareAsync();
    public void Select(string id)
    { if (!IsCurrent || !Selection.Select(id)) Message = _catalog.Text["Scene.Stale"]; }
    public bool Commit()
    {
        if (!IsCurrent || !Selection.TryResolve(out var handle) || Target is null) { Message = _catalog.Text["Scene.Stale"]; return false; }
        return _owner.SelectObject(this, handle);
    }
    public void Dispose() { if (_disposed) return; _disposed = true; Selection.Dispose(); }
}

public sealed partial class TimelineEditSession
{
    private readonly HashSet<string> _selectedObjects = new(StringComparer.Ordinal);
    private readonly HashSet<(string Handle, string Property)> _pendingProperties = [];
    public IEditorLocalizationService SceneText => _localization;
    public TimelineObjectEdit? ObjectEdit { get; private set; }
    public TimelineObjectEdit? BeginObjectEdit()
    {
        if (_disposed || !CanEdit || _document is null || _target?.EntryId is null) return null;
        // Keep the clicked object row attached while its selector is open.
        Canvas.Cancel(); CancelTimingInput(false); CancelTimingInput(true);
        foreach (var row in Rows) row.Cancel();
        EndObjectEdit();
        ObjectEdit = new(this, _targets, _document, _generation, Frame, _target.GroupId, _target.EntryId); OnPropertyChanged(nameof(ObjectEdit)); return ObjectEdit;
    }
    internal bool IsObjectEditCurrent(TimelineObjectEdit draft) => !_disposed && ReferenceEquals(ObjectEdit, draft) && draft.Generation == _generation && ReferenceEquals(draft.Document, _document) && draft.Frame == Frame && _selection.Current == _target;
    internal bool SelectObject(TimelineObjectEdit draft, string handle)
    {
        if (!IsObjectEditCurrent(draft)) return false;
        _selectedObjects.Add(handle); BuildRows(); return true;
    }
    public IReadOnlyList<TimelineValueRow> AvailableProperties(string handle) => Rows.Where(row => row.Handle == handle && !row.HasTrack && row.Capability is not null).ToArray();
    public bool AddProperty(TimelineValueRow row)
    {
        if (_disposed || !CanEdit || !Rows.Contains(row) || row.HasTrack || row.Capability is null) return false;
        if (Tree.FirstOrDefault(node => node.ObjectHandle == row.Handle) is { } root) root.IsExpanded = true;
        if (row.DraftValue is not null) return row.Commit(true);
        _selectedObjects.Add(row.Handle); _pendingProperties.Add((row.Handle, row.Property));
        RebuildVisibleNodes(); Message = _localization["Timeline.InitialUnknown"]; return true;
    }
    private void PruneObjectDrafts()
    {
        _selectedObjects.RemoveWhere(handle => !_scene.Targets.Any(target => target.Handle == handle));
        _pendingProperties.RemoveWhere(item => !_scene.Targets.Any(target => target.Handle == item.Handle)
            || _document?.Tracks.Any(track => track.HandleId == item.Handle && track.Property == item.Property) == true);
    }
    private void ClearObjectDrafts() { _curveViews.Clear(); _selectedObjects.Clear(); _pendingProperties.Clear(); }
    public void EndObjectEdit() { var previous = ObjectEdit; ObjectEdit = null; previous?.Dispose(); OnPropertyChanged(nameof(ObjectEdit)); }
}
