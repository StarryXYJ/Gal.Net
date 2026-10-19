using System.ComponentModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Core.Scene;

namespace GalNet.Editor.Services;

/// <summary>Selection-scoped draft; never holds an author array index without its document generation.</summary>
public sealed partial class TimelineKeyEdit : ObservableObject
{
    private readonly TimelineEditSession _owner;
    internal TimelineDocument Document { get; }
    internal int Generation { get; }
    internal TimelineKeyLocation[] Locations { get; }
    private readonly TimelineKeyframeDefinition[] _keys;
    private bool _perSecond;
    private decimal Factor => _perSecond ? Document.FrameRate : 1;
    public int Count => Locations.Length;
    public bool IsCurrent => _owner.IsKeyEditCurrent(this);
    public string UnitKey => _perSecond ? "Timeline.Key.PerSecond" : "Timeline.Key.PerFrame";
    [ObservableProperty] private decimal? _value;
    [ObservableProperty] private decimal? _incoming;
    [ObservableProperty] private decimal? _outgoing;
    [ObservableProperty] private int _interpolationIndex;
    internal TimelineKeyEdit(TimelineEditSession owner, TimelineDocument document, int generation, TimelineKeyLocation[] locations, bool perSecond)
    {
        _owner = owner; Document = document; Generation = generation; Locations = locations; _perSecond = perSecond;
        _keys = locations.Select(location => document.Tracks[location.Track].Keys.Single(key => key.Frame == location.Frame)).ToArray();
        Value = Mixed(_keys.Select(key => key.Value)); Incoming = Scale(Mixed(_keys.Select(key => key.InTangent)), Factor);
        Outgoing = Scale(Mixed(_keys.Select(key => key.OutTangent)), Factor);
        InterpolationIndex = _keys.Select(key => key.InterpolationToNext).Distinct().Count() == 1 ? (int)_keys[0].InterpolationToNext : -1;
    }
    private static decimal? Mixed(IEnumerable<float> values)
    { var numbers = values.Distinct().Take(2).ToArray(); try { return numbers.Length == 1 ? (decimal)numbers[0] : null; } catch (OverflowException) { return null; } }
    private static decimal? Scale(decimal? value, decimal factor) { try { return value * factor; } catch (OverflowException) { return null; } }
    internal void ConvertUnits(bool perSecond)
    {
        if (_perSecond == perSecond) return;
        var oldFactor = Factor; _perSecond = perSecond;
        Incoming = Scale(Incoming, Factor / oldFactor); Outgoing = Scale(Outgoing, Factor / oldFactor); OnPropertyChanged(nameof(UnitKey));
    }
    public void Cancel()
    {
        Value = Mixed(_keys.Select(key => key.Value)); Incoming = Scale(Mixed(_keys.Select(key => key.InTangent)), Factor);
        Outgoing = Scale(Mixed(_keys.Select(key => key.OutTangent)), Factor);
        InterpolationIndex = _keys.Select(key => key.InterpolationToNext).Distinct().Count() == 1 ? (int)_keys[0].InterpolationToNext : -1;
    }
    public bool CommitValue() => IsCurrent && Value is { } value && (Value == Mixed(_keys.Select(key => key.Value)) || _owner.EditSelectedKeys(this, value: (float)value));
    public bool CommitIncoming() => IsCurrent && Incoming is { } value && (Incoming == Scale(Mixed(_keys.Select(key => key.InTangent)), Factor) || _owner.EditSelectedKeys(this, incoming: (float)(value / Factor)));
    public bool CommitOutgoing() => IsCurrent && Outgoing is { } value && (Outgoing == Scale(Mixed(_keys.Select(key => key.OutTangent)), Factor) || _owner.EditSelectedKeys(this, outgoing: (float)(value / Factor)));
    public bool CommitInterpolation()
    {
        if (!IsCurrent || InterpolationIndex is < 0 or > 2) return false;
        if (_keys.All(key => (int)key.InterpolationToNext == InterpolationIndex)) return true;
        if (_owner.EditSelectedKeys(this, interpolation: (AnimationInterpolation)InterpolationIndex)) return true;
        Cancel(); return false;
    }
    public bool ApplyPreset(TimelineTangentPreset preset) => _owner.EditSelectedKeys(this, preset: preset);
}

public sealed partial class TimelineEditSession
{
    [ObservableProperty] private TimelineKeyEdit? _keyEdit;
    [ObservableProperty] private bool _tangentsPerSecond;
    public bool HasKeySelection => KeyEdit is not null;
    partial void OnKeyEditChanged(TimelineKeyEdit? value) => OnPropertyChanged(nameof(HasKeySelection));
    partial void OnTangentsPerSecondChanged(bool value) => KeyEdit?.ConvertUnits(value);
    private void OnCanvasSelection(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName == nameof(Canvas.Selection)) RefreshKeyEdit(); }
    private void RefreshKeyEdit()
    {
        var selected = Canvas.Selection.Where(_keyLocations.ContainsKey).Select(id => _keyLocations[id]).OrderBy(key => key.Track).ThenBy(key => key.Frame).ToArray();
        KeyEdit = !_disposed && _document is not null && selected.Length > 0 ? new(this, _document, _generation, selected, TangentsPerSecond) : null;
    }
    internal bool IsKeyEditCurrent(TimelineKeyEdit draft) => !_disposed && ReferenceEquals(KeyEdit, draft) && ReferenceEquals(_document, draft.Document) && draft.Generation == _generation;
    internal bool EditSelectedKeys(TimelineKeyEdit draft, float? value = null, float? incoming = null, float? outgoing = null,
        AnimationInterpolation? interpolation = null, TimelineTangentPreset? preset = null)
    {
        if (!IsKeyEditCurrent(draft)) return false;
        try
        {
            if (value is { } number && draft.Locations.Any(location => Rows.FirstOrDefault(row => row.TrackIndex == location.Track)?.Capability is { } capability
                    && _document!.Tracks[location.Track].BlendMode != AnimationBlendMode.Additive && !capability.Accepts(number)))
            { Message = _localization["Timeline.ValueRange"]; return false; }
            var patch = _document!.EditKeys(draft.Locations, value, incoming, outgoing, interpolation, preset);
            if (_baseline is { } original && JsonElement.DeepEquals(original, patch)) return true;
            var locations = draft.Locations;
            if (!Submit(patch)) return false;
            Canvas.Select(locations.Select(location => _keyIds[location])); return true;
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or OverflowException or InvalidOperationException)
        { Message = _localization["Timeline.EditRejected"]; return false; }
    }
}
