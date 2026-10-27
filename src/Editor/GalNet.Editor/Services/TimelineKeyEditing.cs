using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Game.Controls.Timeline;

namespace GalNet.Editor.Services;

public sealed partial class TimelineEditSession
{
    public TimelineCanvasModel Canvas { get; } = new();
    public ObservableCollection<TimelineTreeNode> VisibleNodes { get; } = [];
    private readonly Dictionary<Guid, TimelineKeyLocation> _keyLocations = [];
    private readonly Dictionary<TimelineKeyLocation, Guid> _keyIds = [];
    private TimelineDocument? _keyDocument, _moveDocument;
    private ActiveEntrySelection? _moveTarget;
    private TimelineKeyLocation[]? _moveKeys;
    private string? _activeHandle, _activeProperty;
    [ObservableProperty] private decimal? _timingDurationDraft = 60;
    [ObservableProperty] private decimal? _timingFrameRateDraft = 60;
    public int MinimumDuration => _document?.MinimumDuration ?? 0;
    private void InitializeCanvas()
    {
        Canvas.PropertyChanged += OnCanvasSelection;
        Canvas.SeekRequested += SeekCanvas;
        Canvas.BeginMove = BeginKeyMove; Canvas.PreviewMove = PreviewKeyMove; Canvas.CommitMove = delta => CommitKeyMove(delta, false);
        Canvas.BeginCurve = BeginKeyMove; Canvas.PreviewCurve = PreviewCurve; Canvas.CommitCurve = CommitCurve;
        Canvas.CancelMove = () => { _moveDocument = null; _moveKeys = null; };
        Canvas.DeleteRequested = DeleteSelectedKeys; Canvas.AddRequested = AddCurrentKey;
    }
    private void SeekCanvas(int frame) => Frame = frame;
    private void InvalidateKeyDocument()
    {
        Canvas.Cancel(); Canvas.Invalidate(); _moveDocument = null; _moveKeys = null; _keyDocument = null;
        _keyIds.Clear(); _keyLocations.Clear(); VisibleNodes.Clear(); Canvas.Rows = [];
    }
    private void SyncCanvasDocument()
    {
        if (!ReferenceEquals(_keyDocument, _document))
        {
            InvalidateKeyDocument(); _keyDocument = _document;
            if (_document is not null)
                for (var track = 0; track < _document.Tracks.Count; track++) foreach (var key in _document.Tracks[track].Keys)
                { var location = new TimelineKeyLocation(track, key.Frame); var id = Guid.NewGuid(); _keyIds.Add(location, id); _keyLocations.Add(id, location); }
            TimingDurationDraft = Duration; TimingFrameRateDraft = FrameRate;
        }
        Canvas.Duration = Duration; Canvas.Frame = Frame; OnPropertyChanged(nameof(MinimumDuration));
        RebuildVisibleNodes();
        RefreshKeyEdit();
    }
    private void DetachTree()
    { foreach (var node in Tree.SelectMany(node => node.Descendants())) node.PropertyChanged -= OnTreeExpansion; }
    private void OnTreeExpansion(object? sender, PropertyChangedEventArgs args)
    { if (args.PropertyName == nameof(TimelineTreeNode.IsExpanded)) { Canvas.Cancel(); RebuildVisibleNodes(); } }
    private void RebuildVisibleNodes()
    {
        VisibleNodes.Clear();
        bool Include(TimelineTreeNode node) => node.Value is { } row ? row.HasTrack || _pendingProperties.Contains((row.Handle, row.Property))
            : node.ObjectHandle is { } handle && _selectedObjects.Contains(handle) || node.Children.Any(Include);
        void Add(TimelineTreeNode node, int depth)
        {
            if (!Include(node)) return;
            node.Depth = depth; VisibleNodes.Add(node);
            if (node.IsExpanded) foreach (var child in node.Children) Add(child, depth + 1);
        }
        foreach (var root in Tree) Add(root, 0);
        IReadOnlyList<TimelineCanvasKey> Keys(TimelineTreeNode node) => node.Descendants().Where(child => child.Value?.HasTrack == true)
            .SelectMany(child => _keyIds.Where(pair => pair.Key.Track == child.Value!.TrackIndex).Select(pair => CanvasKey(pair.Value, _document!.Tracks[pair.Key.Track].Keys.Single(key => key.Frame == pair.Key.Frame)))).ToArray();
        Canvas.Rows = VisibleNodes.Select(node => { var keys = Keys(node); return new TimelineCanvasRow(keys, !node.IsLeaf, node.HasCurve ? CurveView(node, keys) : null); }).ToArray();
        OnPropertyChanged(nameof(HasNoRows));
    }
    public void SetActiveTrack(TimelineValueRow row)
    { if (!Rows.Contains(row)) return; _activeHandle = row.Handle; _activeProperty = row.Property; Canvas.Select([]); }
    private int[] SelectedTracks() => Canvas.Selection.Where(_keyLocations.ContainsKey).Select(id => _keyLocations[id].Track).Distinct().ToArray() is { Length: > 0 } selected ? selected
        : Rows.FirstOrDefault(row => row.Handle == _activeHandle && row.Property == _activeProperty && row.HasTrack) is { } active ? [active.TrackIndex] : [];
    private bool BeginKeyMove()
    {
        var keys = Canvas.Selection.Where(_keyLocations.ContainsKey).Select(id => _keyLocations[id]).ToArray();
        if (!CanEdit || _document is null || keys.Length == 0) return false;
        _moveKeys = keys; _moveDocument = _document; _moveTarget = _target; Message = ""; return true;
    }
    private bool IsMoveCurrent => !_disposed && _moveDocument is not null && ReferenceEquals(_moveDocument, _document) && _moveTarget == _target && _moveTarget == _selection.Current && _moveKeys is { Length: > 0 };
    private bool PreviewKeyMove(int delta)
    {
        if (!IsMoveCurrent) return false;
        try { _moveDocument!.ValidateMove(_moveKeys!, delta, false); Message = ""; return true; }
        catch (Exception error) when (error is InvalidDataException or OverflowException) { EditError(error); return false; }
    }
    private bool CommitKeyMove(int delta, bool replace)
    {
        if (!IsMoveCurrent) return false;
        try
        {
            var locations = _moveKeys!; var next = _moveDocument!.MoveKeys(locations, delta, replace);
            if (!Submit(next)) return false;
            Canvas.Select(locations.Select(key => _keyIds.TryGetValue(new(key.Track, key.Frame + delta), out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty));
            return true;
        }
        catch (Exception error) when (error is InvalidDataException or OverflowException or ArgumentException) { EditError(error); return false; }
    }
    public void ReplaceKeyCollisions() { if (Canvas.HasPendingMove && CommitKeyMove(Canvas.PreviewDelta, true)) Canvas.ResetPreview(); }
    private void EditError(Exception error) => Message = _localization[error.Message.StartsWith("Timeline.", StringComparison.Ordinal) ? error.Message : "Timeline.EditRejected"];
    public void DeleteSelectedKeys()
    {
        if (_document is null || Canvas.HasPendingMove) return;
        var selected = Canvas.Selection.Where(_keyLocations.ContainsKey).Select(id => _keyLocations[id]).ToArray(); if (selected.Length == 0) return;
        try { Submit(_document.DeleteKeys(selected)); }
        catch (Exception error) when (error is InvalidDataException or ArgumentException) { EditError(error); }
    }
    public void AddCurrentKey()
    {
        if (_document is null || Canvas.HasPendingMove) return;
        var tracks = SelectedTracks(); if (tracks.Length == 0) { Message = _localization["Timeline.SelectTrack"]; return; }
        try
        {
            var frame = Frame;
            if (Submit(_document.AddKeys(tracks, frame))) Canvas.Select(tracks.Select(track => _keyIds[new(track, frame)]));
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException) { EditError(error); }
    }
    public void NavigateKey(int direction)
    {
        if (_document is null) return;
        var tracks = SelectedTracks(); if (tracks.Length == 0) tracks = Enumerable.Range(0, _document.Tracks.Count).ToArray();
        var frames = tracks.SelectMany(track => _document.Tracks[track].Keys.Select(key => key.Frame)).Concat(_document.Events.Select(item => item.Frame));
        var next = direction < 0 ? frames.Where(frame => frame < Frame).DefaultIfEmpty(0).Max() : frames.Where(frame => frame > Frame).DefaultIfEmpty(Duration).Min(); Frame = next;
    }
    public void CancelTimingInput(bool frameRate)
    { if (frameRate) TimingFrameRateDraft = FrameRate; else TimingDurationDraft = Duration; }
    public void CommitTimingInput(bool frameRate)
    {
        if (_document is null) return;
        var value = frameRate ? TimingFrameRateDraft : TimingDurationDraft;
        if (value is not { } number || number % 1 != 0 || number < (frameRate ? 1 : 0) || number > (frameRate ? 240 : int.MaxValue))
        { Message = _localization["Timeline.EditRejected"]; return; }
        var duration = frameRate ? Duration : Math.Max((int)number, MinimumDuration);
        var fps = frameRate ? (int)number : FrameRate;
        Canvas.Cancel();
        try { if (Submit(_document.ChangeTiming(duration, fps))) CancelTimingInput(frameRate); }
        catch (Exception error) when (error is InvalidDataException or OverflowException or InvalidOperationException or ArgumentException) { EditError(error); }
    }
}
