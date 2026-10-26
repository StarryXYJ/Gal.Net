using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Contracts.Builtins;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Shared.Services;
using GalNet.Game.Controls.Timeline;

namespace GalNet.Editor.Services;

public sealed partial class TimelineEventRow(Guid id, int index, int frame, string type, Func<string> label) : ObservableObject
{
    public Guid Id { get; } = id;
    public int Index { get; } = index;
    public int Frame { get; } = frame;
    public string Type { get; } = type;
    public bool CanEdit => TimelineEventCapabilities.Supports(Type);
    public string Label => label();
    [ObservableProperty] private bool _isSelected;
    internal void RefreshLabel() => OnPropertyChanged(nameof(Label));
}

public sealed partial class TimelineEditSession
{
    public TimelineCanvasModel EventCanvas { get; } = new();
    public ObservableCollection<TimelineEventRow> Events { get; } = [];
    public IReadOnlyList<EntryTypeOption> EventTypes => TimelineEventCapabilities.Types.Where(type => _catalog.TryGet(type, out _)).Select(type => new EntryTypeOption(type, "Timeline", "Entry.Type." + type)).ToArray();
    private TimelineDocument? _eventDocument, _eventMoveDocument;
    private ActiveEntrySelection? _eventMoveTarget;
    private int[] _eventMoveIndices = [];
    private readonly Dictionary<int, Guid> _eventIds = [];
    private bool _syncEventView;
    public TimelineEventEdit? EventEdit { get; private set; }
    private void InitializeEvents()
    {
        Canvas.PropertyChanged += OnEventViewChanged;
        EventCanvas.PropertyChanged += OnEventCanvasChanged;
        EventCanvas.SeekRequested += SeekCanvas;
        EventCanvas.BeginMove = () =>
        {
            var selected = SelectedEvents(); if (_disposed || !CanEdit || _document is null || selected.Length == 0) return false;
            EndEventEdit(); _eventMoveDocument = _document; _eventMoveTarget = _target; _eventMoveIndices = selected; return true;
        };
        EventCanvas.PreviewMove = delta => EventMove(delta, false);
        EventCanvas.CommitMove = delta => EventMove(delta, true);
        EventCanvas.CancelMove = () => { _eventMoveDocument = null; _eventMoveIndices = []; };
        EventCanvas.DeleteRequested = DeleteSelectedEvents;
    }
    private void OnEventViewChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is null or nameof(TimelineCanvasModel.DisplayFrame) or nameof(TimelineCanvasModel.Frame)) EventCanvas.PreviewSeek(Canvas.DisplayFrame);
        if (args.PropertyName == nameof(TimelineCanvasModel.Selection) && Canvas.Selection.Count > 0) { EventCanvas.Cancel(); EventCanvas.Select([]); }
        SyncEventView(false, args.PropertyName);
    }
    private void OnEventCanvasChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(TimelineCanvasModel.Selection))
        {
            if (EventCanvas.Selection.Count > 0) { Canvas.Cancel(); Canvas.Select([]); }
            foreach (var row in Events) row.IsSelected = EventCanvas.Selection.Contains(row.Id);
        }
        SyncEventView(true, args.PropertyName);
    }
    private void SyncEventView(bool fromEvents, string? property)
    {
        if (_syncEventView || property is not (nameof(TimelineCanvasModel.StartFrame) or nameof(TimelineCanvasModel.PixelsPerFrame))) return;
        _syncEventView = true;
        try { var source = fromEvents ? EventCanvas : Canvas; var target = fromEvents ? Canvas : EventCanvas; target.StartFrame = source.StartFrame; target.PixelsPerFrame = source.PixelsPerFrame; }
        finally { _syncEventView = false; }
    }
    private void SyncEvents()
    {
        EndEventEdit(); EventCanvas.Cancel();
        if (!ReferenceEquals(_eventDocument, _document)) { EventCanvas.Invalidate(); _eventIds.Clear(); _eventDocument = _document; }
        Events.Clear();
        if (_document is not null && CanEdit)
            foreach (var item in _document.Events.OrderBy(item => item.Frame).ThenBy(item => item.Index))
            {
                if (!_eventIds.TryGetValue(item.Index, out var id)) _eventIds[item.Index] = id = Guid.NewGuid();
                Events.Add(new(id, item.Index, item.Frame, item.Type, () =>
                {
                    var key = "Entry.Type." + item.Type; var name = _localization[key];
                    if (name == "[" + key + "]") name = item.Type;
                    return _localization.Format("Timeline.Event.Label", item.Frame, name);
                })
                { IsSelected = EventCanvas.Selection.Contains(id) });
            }
        EventCanvas.Duration = Duration; EventCanvas.Frame = Frame; EventCanvas.StartFrame = Canvas.StartFrame; EventCanvas.PixelsPerFrame = Canvas.PixelsPerFrame;
        EventCanvas.Rows = Events.Select(item => new TimelineCanvasRow([new(item.Id, item.Frame)], false)).ToArray();
    }
    private int[] SelectedEvents() => Events.Where(item => EventCanvas.Selection.Contains(item.Id)).Select(item => item.Index).ToArray();
    public void SelectEvent(TimelineEventRow row, bool toggle = false)
    { if (Events.Contains(row)) EventCanvas.Select([row.Id], toggle: toggle); }
    public Func<string, bool> CaptureEventInsertion()
    {
        var document = _document; var target = _target; var generation = _generation; var frame = Frame;
        return type => !_disposed && document is not null && ReferenceEquals(document, _document) && target == _target && generation == _generation && frame == Frame && AddEvent(type);
    }
    public bool AddEvent(string type)
    {
        if (_disposed || !CanEdit || _document is null || !TimelineEventCapabilities.Supports(type) || !_catalog.TryGet(type, out var schema)) return false;
        var parameters = schema.DynamicParameters.Values.Where(parameter => parameter.DefaultValue is not null).ToDictionary(parameter => parameter.Name, parameter => parameter.DefaultValue!.Value.Clone());
        parameters = SceneAuthoringIdentities.NewEntry(type, parameters);
        var index = _document.Events.Count;
        if (!Submit(_document.AddEvent(type, Frame, parameters))) return false;
        if (_eventIds.TryGetValue(index, out var id)) EventCanvas.Select([id]); return true;
    }
    private bool EventMove(int delta, bool commit)
    {
        if (_disposed || _eventMoveDocument is null || !ReferenceEquals(_eventMoveDocument, _document) || _eventMoveTarget != _selection.Current || _eventMoveTarget != _target) return false;
        try
        {
            var indices = _eventMoveIndices;
            if (!commit) { _eventMoveDocument.ValidateEventMove(indices, delta); return true; }
            if (delta == 0) return true;
            var value = _eventMoveDocument.MoveEvents(indices, delta);
            if (!Submit(value)) return false;
            EventCanvas.Select(indices.Where(_eventIds.ContainsKey).Select(index => _eventIds[index])); return true;
        }
        catch (Exception error) when (error is InvalidDataException or OverflowException or ArgumentException) { EditError(error); return false; }
    }
    public void DeleteSelectedEvents()
    {
        if (_disposed || _document is null || EventCanvas.HasPendingMove) return;
        var selected = SelectedEvents(); if (selected.Length > 0) Submit(_document.DeleteEvents(selected));
    }
    public void OrderEvent(TimelineEventRow row, int direction)
    {
        if (_disposed || _document is null || !Events.Contains(row) || direction is not (-1 or 1)) return;
        var peers = _document.Events.Where(item => item.Frame == row.Frame).Select(item => item.Index).ToArray();
        var next = Array.IndexOf(peers, row.Index) + direction;
        if (next < 0 || next >= peers.Length) return;
        var index = peers[next];
        if (Submit(_document.OrderEvent(row.Index, direction)) && _eventIds.TryGetValue(index, out var id)) EventCanvas.Select([id]);
    }
    public TimelineEventEdit? BeginEventEdit(TimelineEventRow row)
    {
        EndEventEdit();
        if (_disposed || !CanEdit || _document is null || !Events.Contains(row) || !TimelineEventCapabilities.Supports(row.Type) || !_catalog.TryGet(row.Type, out var schema)) return null;
        EventCanvas.Cancel(); Canvas.Cancel(); SelectEvent(row);
        try { EventEdit = new(this, _document, _generation, Frame, row.Index, row.Type, schema.FieldDataType, schema.DynamicParameters.Fields); }
        catch (Exception error) when (error is JsonException or InvalidDataException or InvalidOperationException or ArgumentException) { EditError(error); return null; }
        OnPropertyChanged(nameof(EventEdit)); return EventEdit;
    }
    internal bool IsEventEditCurrent(TimelineEventEdit draft) => !_disposed && CanEdit && ReferenceEquals(EventEdit, draft) && ReferenceEquals(_document, draft.Document) && draft.Generation == _generation && draft.Frame == Frame && _target == _selection.Current;
    internal IReadOnlyList<GalNet.Game.Controls.Forms.FieldEditorOption>? EventFieldOptions(int index, string type, string field) => _target?.EntryId is { } id && _document is not null
        ? _targets.FieldOptions(_target.GroupId, id, type, field, new(_document.Events[index].Frame, index)) : null;
    internal bool ApplyEvent(TimelineEventEdit draft) => IsEventEditCurrent(draft) && Submit(draft.Document.PatchEvent(draft.Index, draft.Patch));
    public void EndEventEdit() { var old = EventEdit; EventEdit = null; old?.Dispose(); OnPropertyChanged(nameof(EventEdit)); }
    private void InvalidateEvents()
    {
        EndEventEdit(); EventCanvas.Cancel(); EventCanvas.Invalidate(); Events.Clear(); _eventIds.Clear(); _eventDocument = null;
        EventCanvas.Rows = [];
    }
    private void DisposeEvents()
    {
        InvalidateEvents();
        Canvas.PropertyChanged -= OnEventViewChanged; EventCanvas.PropertyChanged -= OnEventCanvasChanged; EventCanvas.SeekRequested -= SeekCanvas;
        EventCanvas.BeginMove = null; EventCanvas.PreviewMove = null; EventCanvas.CommitMove = null; EventCanvas.CancelMove = null; EventCanvas.DeleteRequested = null;
    }
}
