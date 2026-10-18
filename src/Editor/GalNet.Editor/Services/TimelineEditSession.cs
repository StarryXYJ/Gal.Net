using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Core.Scene;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;

namespace GalNet.Editor.Services;

/// <summary>Project-owned Timeline binding. Rows capture identity, frame and revision, never a page.</summary>
public sealed partial class TimelineEditSession : ObservableObject, IDisposable
{
    private readonly EntrySelectionContext _selection;
    private readonly IEditorDocumentService _documents;
    private readonly EntryFieldAuthoringService _authoring;
    private readonly IEntryCatalog _catalog;
    private readonly IEditorLocalizationService _localization;
    private readonly EditorHistories _histories;
    private string? _type;
    private EditorEntryData? _entry;
    private TimelineDocument? _document;
    private ActiveEntrySelection? _target;
    private bool _disposed;
    private int _generation;
    private int _frame;
    private JsonElement? _baseline;
    public ObservableCollection<TimelineValueRow> Rows { get; } = [];
    [ObservableProperty] private string _statusKey = "Timeline.SelectEntry";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private string _source = "";
    [ObservableProperty] private bool _canCreate;
    [ObservableProperty] private bool _canEdit;
    [ObservableProperty] private int _duration;
    [ObservableProperty] private int _frameRate = 60;
    public bool HasNoRows => CanEdit && Rows.Count == 0;
    public int Frame
    {
        get => _frame;
        set
        {
            var next = Math.Clamp(value, 0, Duration);
            if (SetProperty(ref _frame, next)) BuildRows();
        }
    }
    public TimelineEditSession(EntrySelectionContext selection, IEditorDocumentService documents, EntryFieldAuthoringService authoring,
        IEntryCatalog catalog, IEditorLocalizationService localization, EditorHistories histories)
    {
        _selection = selection; _documents = documents; _authoring = authoring; _catalog = catalog; _localization = localization; _histories = histories;
        selection.Changed += OnSelection; documents.DocumentChanged += Refresh; histories.Changed += Refresh;
        localization.PropertyChanged += OnLocale;
        Refresh();
    }
    private void OnSelection() { _frame = 0; OnPropertyChanged(nameof(Frame)); Refresh(); }
    private void OnLocale(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != "Item[]") return;
        foreach (var row in Rows) row.RefreshLabel();
        if (Message.Length != 0) Message = _localization["Timeline.EditRejected"];
    }
    private EditorEntryData? Find(ActiveEntrySelection? target) => !_documents.IsLoaded || target?.EntryId is null
        || !_documents.CurrentDocument.Nodes.Any(node => node.Id == target.GroupId && node.Type == "Group")
        || !_documents.Groups.TryGetValue(target.GroupId, out var group) ? null : group.Entries.SingleOrDefault(entry => entry.StableId == target.EntryId);
    private void Refresh()
    {
        if (_disposed) return;
        var next = Find(_selection.Current);
        var root = next?.Parameters.TryGetValue("timeline", out var value) == true ? (JsonElement?)value.Clone() : null;
        var sameValue = _baseline is { } previous ? root is { } current && JsonElement.DeepEquals(previous, current) : root is null;
        if (_target == _selection.Current && ReferenceEquals(_entry, next) && _type == next?.Type && sameValue)
        { RefreshSource(); return; }
        _generation++; Rows.Clear(); _document = null; _baseline = null; Message = "";
        _target = _selection.Current; _entry = next; _type = next?.Type; _baseline = root;
        CanCreate = false; CanEdit = false; Duration = 0; FrameRate = 60;
        RefreshSource();
        StatusKey = _entry is null ? "Timeline.SelectEntry" : "Timeline.Unsupported";
        if (_entry?.Type != BuiltinEntryIds.PlayTimeline || !_catalog.TryGet(_entry.Type, out var schema)
            || schema.DynamicParameters?.TryGetValue("timeline", out var field) != true || field.Value.ValueType != typeof(JsonElement))
        { OnPropertyChanged(nameof(HasNoRows)); return; }
        if (!_entry.Parameters.TryGetValue("timeline", out var json) || json.ValueKind == JsonValueKind.Null)
        { CanCreate = true; StatusKey = "Timeline.Missing"; OnPropertyChanged(nameof(HasNoRows)); return; }
        _baseline = json.Clone();
        try
        {
            _document = new(json); Duration = _document.Duration; FrameRate = _document.FrameRate; CanEdit = true; StatusKey = "Timeline.Editing";
            _frame = Math.Clamp(_frame, 0, Duration); OnPropertyChanged(nameof(Frame)); BuildRows();
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or InvalidDataException or OverflowException)
        { StatusKey = "Timeline.Invalid"; OnPropertyChanged(nameof(HasNoRows)); }
    }
    private void RefreshSource() => Source = _entry is null ? "" :
        (_documents.CurrentDocument.Nodes.Single(node => node.Id == _target!.GroupId).Name) + " / " + _entry.Id;
    private void BuildRows()
    {
        _generation++; Rows.Clear();
        if (_document is not null && CanEdit)
        {
            var objects = _document.Tracks.Select(track => track.HandleId).Distinct(StringComparer.Ordinal).ToArray();
            for (var index = 0; index < _document.Tracks.Count; index++)
            {
                var track = _document.Tracks[index];
                var value = TimelineSampler.Evaluate(track, Frame);
                Rows.Add(new(this, _generation, index, Frame, System.Array.IndexOf(objects, track.HandleId) + 1, track.Property,
                    track.BlendMode == AnimationBlendMode.Additive, track.Keys.Any(key => key.Frame == Frame), value, _localization));
            }
        }
        OnPropertyChanged(nameof(HasNoRows));
    }
    public void Create()
    {
        if (!CanCreate || _disposed || !ReferenceEquals(_entry, Find(_target))) return;
        Submit(JsonSerializer.SerializeToElement(new
        {
            playbackHandleId = Guid.NewGuid().ToString("N"),
            frameRate = 60,
            durationFrames = 60,
            tracks = System.Array.Empty<object>(),
            events = System.Array.Empty<object>()
        }));
    }
    internal bool Commit(TimelineValueRow row, int generation, int index, int frame, decimal? value)
    {
        if (_disposed || generation != _generation || frame != Frame || !Rows.Contains(row) || _document is null || value is null) return false;
        try { return Submit(_document.Upsert(index, frame, (float)value.Value)); }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or InvalidDataException or OverflowException)
        { Message = _localization["Timeline.EditRejected"]; return false; }
    }
    private bool Submit(JsonElement value)
    {
        if (_entry is null || !ReferenceEquals(_entry, Find(_target)) || _entry.Type != BuiltinEntryIds.PlayTimeline || _selection.Current != _target) return false;
        var current = _entry.Parameters.TryGetValue("timeline", out var json) ? (JsonElement?)json : null;
        if (_baseline is { } expected ? current is not { } actual || !JsonElement.DeepEquals(expected, actual)
            : current is { ValueKind: not JsonValueKind.Null }) { Refresh(); return false; }
        var parameters = _entry.Parameters.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);
        parameters["timeline"] = value.Clone();
        try
        {
            _catalog.Get(_entry.Type).DynamicParameters!.ValidatePartialArguments(JsonSerializer.SerializeToElement(parameters));
            _authoring.SetParameters(_target!.GroupId, _entry, _entry.Type, parameters); return true;
        }
        catch (Exception error) { Message = EditorText.Error(error).Render(_localization); return false; }
    }
    public void CancelInput() { if (!_disposed) BuildRows(); }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _generation++; Rows.Clear();
        _selection.Changed -= OnSelection; _documents.DocumentChanged -= Refresh; _histories.Changed -= Refresh;
        _localization.PropertyChanged -= OnLocale; _entry = null; _document = null;
    }
}

public sealed partial class TimelineValueRow : ObservableObject
{
    private readonly TimelineEditSession _session;
    private readonly int _generation, _index, _frame, _objectNumber;
    private readonly IEditorLocalizationService _localization;
    private readonly bool _offset, _hasKey;
    private readonly decimal? _original;
    public string Property { get; }
    public string ObjectLabel => _localization.Format("Timeline.Object", _objectNumber);
    public string Hint => _localization[_offset ? "Timeline.Offset" : _hasKey ? "Timeline.KeyHere" : "Timeline.Sampled"];
    public bool IsEditable { get; }
    [ObservableProperty] private decimal? _draftValue;
    internal TimelineValueRow(TimelineEditSession session, int generation, int index, int frame, int objectNumber,
        string property, bool offset, bool hasKey, float value, IEditorLocalizationService localization)
    {
        _session = session; _generation = generation; _index = index; _frame = frame; _objectNumber = objectNumber;
        Property = property; _offset = offset; _hasKey = hasKey; _localization = localization;
        try { _original = (decimal)value; _draftValue = _original; IsEditable = float.IsFinite(value); }
        catch (OverflowException) { IsEditable = false; }
    }
    public bool Commit() => DraftValue != _original && _session.Commit(this, _generation, _index, _frame, DraftValue);
    public void Cancel() => DraftValue = _original;
    internal void RefreshLabel() { OnPropertyChanged(nameof(ObjectLabel)); OnPropertyChanged(nameof(Hint)); }
}
