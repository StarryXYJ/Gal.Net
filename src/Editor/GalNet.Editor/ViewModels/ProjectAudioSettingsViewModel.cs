using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Contracts.Builtins.Audio;
using GalNet.Core.Parameters;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Services;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.ViewModels;

/// <summary>One list of buses, each containing its tracks. Input drafts belong to the settings page.</summary>
public sealed class ProjectAudioSettingsViewModel : ObservableObject, IDisposable
{
    private readonly ConfigurationSectionEditor _section;
    private readonly Action _changed;
    private readonly IFieldBinding _buses, _tracks;
    private readonly FieldDescriptor _busElement, _trackElement;
    private readonly List<AudioSettingsItemViewModel> _allTracks = [];
    private JsonElement? _busSnapshot, _trackSnapshot;
    private bool _publishing, _disposed, _flushing;
    public IEditorLocalizationService L { get; }
    public ObservableCollection<AudioSettingsItemViewModel> Buses { get; } = [];
    public ObservableCollection<AudioSettingsItemViewModel> UnassignedTracks { get; } = [];
    public bool IsSupported { get; private set; }
    public bool IsEnabled => !_disposed && IsSupported && _buses.IsEnabled && _tracks.IsEnabled;
    public bool HasInputDraft => Buses.Concat(_allTracks).Any(item => item.HasInputDraft);
    public bool HasUnassignedTracks => UnassignedTracks.Count != 0 || Buses.Count == 0;
    public bool IsExpanded { get; set; } = true;
    public string? Error { get; private set; }
    public IAsyncRelayCommand AddBusCommand { get; }
    public IAsyncRelayCommand AddUnassignedTrackCommand { get; }

    public ProjectAudioSettingsViewModel(ConfigurationSectionEditor section, IEditorLocalizationService localization, Action changed)
    {
        _section = section; L = localization; _changed = changed;
        var busField = section.Item.Fields.Single(field => field.Id == "audioBuses");
        var trackField = section.Item.Fields.Single(field => field.Id == "audioTracks");
        _buses = section.Binding(busField); _tracks = section.Binding(trackField);
        using var resolver = new ValueStructureResolver();
        _busElement = resolver.Resolve(busField).Element!;
        _trackElement = resolver.Resolve(trackField).Element!;
        AddBusCommand = new AsyncRelayCommand(() => RunAsync(() => Add(false, null)));
        AddUnassignedTrackCommand = new AsyncRelayCommand(() => RunAsync(() => Add(true, null)));
        _buses.Changed += SourceChanged; _tracks.Changed += SourceChanged;
        L.PropertyChanged += LocaleChanged;
        Reload();
    }
    private static JsonElement[]? Read(IFieldBinding binding) => binding.Read().Json is { ValueKind: JsonValueKind.Array } array
        ? array.EnumerateArray().Select(item => item.Clone()).ToArray() : null;
    private void Reload()
    {
        _busSnapshot = _buses.Read().Json; _trackSnapshot = _tracks.Read().Json;
        foreach (var item in Buses.Concat(_allTracks)) item.Release();
        Buses.Clear(); _allTracks.Clear();
        var buses = Read(_buses); var tracks = Read(_tracks);
        IsSupported = buses is not null && tracks is not null && buses.All(item => item.ValueKind == JsonValueKind.Object)
            && tracks.All(item => item.ValueKind == JsonValueKind.Object);
        if (IsSupported)
        {
            foreach (var value in buses!) Buses.Add(Create(false, value));
            foreach (var value in tracks!) _allTracks.Add(Create(true, value));
        }
        Regroup();
        OnPropertyChanged(nameof(IsSupported)); OnPropertyChanged(nameof(IsEnabled));
    }
    private AudioSettingsItemViewModel Create(bool track, JsonElement value) =>
        new(this, track, track ? _trackElement : _busElement, value, _section.Registry, _section, _changed);
    private void SourceChanged(object? sender, EventArgs args)
    {
        static bool Equal(JsonElement? left, JsonElement? right) => left is { } a ? right is { } b && JsonElement.DeepEquals(a, b) : right is null;
        if (!_disposed && !_publishing && (!Equal(_busSnapshot, _buses.Read().Json) || !Equal(_trackSnapshot, _tracks.Read().Json))) Reload();
    }
    private void LocaleChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != "Item[]") return;
        foreach (var item in Buses.Concat(_allTracks)) item.Refresh();
    }
    internal void Regroup()
    {
        var assigned = new HashSet<AudioSettingsItemViewModel>();
        foreach (var bus in Buses)
        {
            var matches = _allTracks.Where(track => bus.Id.Length > 0 && track.BusId == bus.Id && assigned.Add(track)).ToArray();
            Replace(bus.Tracks, matches); bus.Refresh();
        }
        Replace(UnassignedTracks, _allTracks.Where(track => !assigned.Contains(track)).ToArray());
        OnPropertyChanged(nameof(HasUnassignedTracks));
    }
    private static void Replace(ObservableCollection<AudioSettingsItemViewModel> target, AudioSettingsItemViewModel[] values)
    {
        if (target.SequenceEqual(values)) return;
        target.Clear(); foreach (var value in values) target.Add(value);
    }
    internal bool Contains(AudioSettingsItemViewModel item) => !_disposed && (item.IsTrack ? _allTracks.Contains(item) : Buses.Contains(item));
    internal ValueIssue? Commit(AudioSettingsItemViewModel item, FieldDescriptor field, FieldValue value)
    {
        if (!Contains(item) || !IsEnabled) return new("settings.obsolete", L["Settings.Project.Audio.Obsolete"]);
        using var document = new ValueDocument(item.Descriptor, item.Value);
        var patch = document.Patch([field.Id], value.Json);
        if (patch.Issue is not null || patch.Value is not { } candidate) return patch.Issue;
        if (field.Id == "id")
        {
            var id = AudioSettingsItemViewModel.ReadString(candidate, "id");
            if (string.IsNullOrWhiteSpace(id) || (item.IsTrack ? _allTracks : Buses.AsEnumerable()).Any(other => !ReferenceEquals(other, item) && other.Id == id))
                return new("settings.audioId", L["Settings.Project.Audio.UniqueId"]);
        }
        var before = item.Value;
        var affected = !item.IsTrack && field.Id == "id" ? _allTracks.Where(track => track.BusId == item.Id).ToArray() : [];
        var trackBefore = affected.Select(track => track.Value).ToArray();
        item.SetValue(candidate);
        for (var i = 0; i < affected.Length; i++) affected[i].SetValue(Set(affected[i].Value, "busId", JsonSerializer.SerializeToElement(item.Id)));
        var issue = Publish();
        if (issue is not null)
        {
            item.SetValue(before);
            for (var i = 0; i < affected.Length; i++) affected[i].SetValue(trackBefore[i]);
        }
        Regroup(); return issue;
    }
    private ValueIssue? Publish()
    {
        _publishing = true;
        try
        {
            var issue = _section.StageAudio(JsonSerializer.SerializeToElement(Buses.Select(bus => bus.Value)), JsonSerializer.SerializeToElement(_allTracks.Select(track => track.Value)));
            _busSnapshot = _buses.Read().Json; _trackSnapshot = _tracks.Read().Json;
            return issue;
        }
        finally { _publishing = false; }
    }
    private void PublishChange()
    {
        if (Publish() is not { } issue) return;
        Reload(); throw new EditorOperationException(issue.Code, issue.Message);
    }
    private static JsonElement Set(JsonElement value, string key, JsonElement replacement)
    {
        var members = value.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
        members[key] = replacement; return JsonSerializer.SerializeToElement(members);
    }
    private string NextId(bool track, string prefix)
    {
        var ids = (track ? _allTracks : Buses.AsEnumerable()).Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        for (var i = 1; ; i++) { var id = prefix + i.ToString(System.Globalization.CultureInfo.InvariantCulture); if (!ids.Contains(id)) return id; }
    }
    private void Add(bool track, AudioSettingsItemViewModel? bus)
    {
        var id = NextId(track, track ? "track" : "bus");
        var descriptor = track ? _trackElement : _busElement;
        var value = track
            ? JsonSerializer.SerializeToElement(new AudioTrackDefinition(id, busId: bus?.Id), descriptor.Value.JsonOptions)
            : JsonSerializer.SerializeToElement(new AudioBusDefinition(id), descriptor.Value.JsonOptions);
        var item = Create(track, value); item.IsExpanded = true;
        if (track) _allTracks.Add(item); else Buses.Add(item);
        PublishChange(); Regroup();
        if (bus is not null) bus.IsExpanded = true;
    }
    internal Task AddTrackAsync(AudioSettingsItemViewModel bus) => RunAsync(() => { if (Contains(bus)) Add(true, bus); });
    internal Task RemoveAsync(AudioSettingsItemViewModel item) => RunAsync(() =>
    {
        if (!Contains(item)) return;
        if (item.IsTrack) _allTracks.Remove(item);
        else
        {
            Buses.Remove(item);
            foreach (var track in _allTracks.Where(track => track.BusId == item.Id))
                track.SetValue(Set(track.Value, "busId", JsonSerializer.SerializeToElement<string?>(null)));
        }
        item.Release(); PublishChange(); Regroup();
    });
    internal Task CopyAsync(AudioSettingsItemViewModel item) => RunAsync(() =>
    {
        if (!Contains(item)) return;
        var copy = Create(item.IsTrack, Set(item.Value, "id", JsonSerializer.SerializeToElement(NextId(item.IsTrack, item.Id + "_"))));
        copy.IsExpanded = true;
        if (item.IsTrack) _allTracks.Insert(_allTracks.IndexOf(item) + 1, copy);
        else Buses.Insert(Buses.IndexOf(item) + 1, copy);
        PublishChange(); Regroup();
    });
    internal Task MoveAsync(AudioSettingsItemViewModel item, int offset) => RunAsync(() =>
    {
        if (!Contains(item)) return;
        if (!item.IsTrack)
        {
            var index = Buses.IndexOf(item); var next = index + offset;
            if (next >= 0 && next < Buses.Count) Buses.Move(index, next);
        }
        else
        {
            var siblings = _allTracks.Where(track => track.BusId == item.BusId).ToArray();
            var next = Array.IndexOf(siblings, item) + offset;
            if (next < 0 || next >= siblings.Length) return;
            var index = _allTracks.IndexOf(item); var target = _allTracks.IndexOf(siblings[next]);
            (_allTracks[index], _allTracks[target]) = (_allTracks[target], _allTracks[index]);
        }
        PublishChange(); Regroup();
    });
    private async Task RunAsync(Action change)
    {
        try
        {
            if (!IsEnabled) return;
            await FlushAsync();
            if (!IsEnabled) return;
            change(); Error = null;
        }
        catch (Exception error) { Error = error.Message; }
        OnPropertyChanged(nameof(Error));
    }
    public async Task FlushAsync()
    {
        if (_flushing) return;
        _flushing = true;
        try
        {
            foreach (var item in Buses.Concat(_allTracks).ToArray()) await item.FlushAsync();
        }
        finally { _flushing = false; }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _buses.Changed -= SourceChanged; _tracks.Changed -= SourceChanged; L.PropertyChanged -= LocaleChanged;
        foreach (var item in Buses.Concat(_allTracks)) item.Release();
    }
}

public sealed class AudioSettingsItemViewModel : ObservableObject
{
    private readonly ProjectAudioSettingsViewModel _owner;
    private readonly Dictionary<string, MemberBinding> _bindings;
    private bool _released, _isExpanded;
    public bool IsTrack { get; }
    internal FieldDescriptor Descriptor { get; }
    internal JsonElement Value { get; private set; }
    public EditorRegistry Registry { get; }
    public DtoEditorItem CommonForm { get; }
    public DtoEditorItem AdvancedForm { get; }
    public bool HasAdvanced => AdvancedForm.Fields.Count != 0;
    public ObservableCollection<AudioSettingsItemViewModel> Tracks { get; } = [];
    public string Id => ReadString(Value, "id");
    internal string? BusId => Value.TryGetProperty("busId", out var bus) && bus.ValueKind == JsonValueKind.String ? bus.GetString() : null;
    public string Title
    {
        get
        {
            var name = ReadString(Value, "name");
            if (string.IsNullOrWhiteSpace(name)) return Id;
            var key = name switch { "Settings.BgmVolume" => "Settings.Project.Audio.Bgm", "Settings.SfxVolume" => "Settings.Project.Audio.Sfx", "Settings.VoiceVolume" => "Settings.Project.Audio.Voice", _ => name };
            var localized = _owner.L[key];
            return localized == "[" + key + "]" ? name : localized;
        }
    }
    public string Summary => IsTrack
        ? string.Join(" · ", EnumLabel("playbackOrder", "Sequential"), EnumLabel("repeat", "None"),
            (Number("defaultGain", 1) * 100).ToString("0.##", System.Globalization.CultureInfo.CurrentCulture) + "%")
        : _owner.L.Format("Settings.Project.Audio.TrackCount", Tracks.Count);
    public bool IsExpanded { get => _isExpanded; set => SetProperty(ref _isExpanded, value); }
    public bool HasInputDraft => _bindings.Values.Any(binding => binding.Draft.IsDirty);
    public IAsyncRelayCommand AddTrackCommand { get; }
    public IAsyncRelayCommand RemoveCommand { get; }
    public IAsyncRelayCommand CopyCommand { get; }
    public IAsyncRelayCommand MoveUpCommand { get; }
    public IAsyncRelayCommand MoveDownCommand { get; }

    internal AudioSettingsItemViewModel(ProjectAudioSettingsViewModel owner, bool track, FieldDescriptor descriptor, JsonElement value,
        EditorRegistry registry, ConfigurationSectionEditor section, Action changed)
    {
        _owner = owner; IsTrack = track; Descriptor = descriptor; Value = value; Registry = registry;
        using var resolver = new ValueStructureResolver();
        var defaults = track ? JsonSerializer.SerializeToElement(new AudioTrackDefinition(), descriptor.Value.JsonOptions)
            : JsonSerializer.SerializeToElement(new AudioBusDefinition(), descriptor.Value.JsonOptions);
        var fields = resolver.Resolve(descriptor).Members.Select(member =>
        {
            var field = member.Field;
            return new FieldDescriptor(field.Id, field.Value, isRequired: field.Id == "id",
                defaultValue: field.Id == "id" ? null : defaults.GetProperty(field.Id), editor: field.Editor,
                labelKey: !track && field.Id == "name" ? "Settings.Project.Audio.BusName" : !track && field.Id == "id" ? "Settings.Project.Audio.BusId" : field.LabelKey);
        }).ToArray();
        _bindings = fields.ToDictionary(field => field.Id, field => new MemberBinding(this, field, section, changed), StringComparer.Ordinal);
        var order = track ? new[] { "name", "id", "playbackOrder", "repeat", "defaultGain", "maxVoices" } : ["name", "id", "defaultGain"];
        CommonForm = new("audio.common", null, order.Select(id => fields.Single(field => field.Id == id)).ToArray(), field => _bindings[field.Id]);
        AdvancedForm = new("audio.advanced", null, fields.Where(field => !order.Contains(field.Id)).ToArray(), field => _bindings[field.Id]);
        AddTrackCommand = new AsyncRelayCommand(() => owner.AddTrackAsync(this));
        RemoveCommand = new AsyncRelayCommand(() => owner.RemoveAsync(this));
        CopyCommand = new AsyncRelayCommand(() => owner.CopyAsync(this));
        MoveUpCommand = new AsyncRelayCommand(() => owner.MoveAsync(this, -1));
        MoveDownCommand = new AsyncRelayCommand(() => owner.MoveAsync(this, 1));
    }
    internal static string ReadString(JsonElement value, string key) => value.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() ?? "" : "";
    private double Number(string key, double fallback) => Value.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) ? number : fallback;
    private string EnumLabel(string key, string fallback) => _owner.L["Settings.Project.Audio." + (ReadString(Value, key) is { Length: > 0 } text ? text : fallback)];
    internal void SetValue(JsonElement value) { Value = value.Clone(); Refresh(); }
    internal void Refresh()
    {
        OnPropertyChanged(nameof(Title)); OnPropertyChanged(nameof(Id)); OnPropertyChanged(nameof(Summary));
        foreach (var binding in _bindings.Values) binding.Refresh();
    }
    internal async Task FlushAsync()
    {
        foreach (var binding in _bindings.Values.Where(binding => binding.Draft.IsDirty).ToArray())
        {
            using var context = new FieldEditorContext(binding.Field, binding, Registry);
            if (!await context.CommitTextAsync()) throw new EditorOperationException("settings.invalid", context.IssueMessage ?? _owner.L["Settings.Project.Audio.InvalidInput"]);
        }
    }
    internal void Release()
    {
        _released = true; foreach (var binding in _bindings.Values) binding.Release();
    }
    private sealed class MemberBinding : IFieldBinding
    {
        private readonly AudioSettingsItemViewModel _item;
        private readonly ConfigurationSectionEditor _section;
        private readonly Action _changed;
        public FieldDescriptor Field { get; }
        public FieldDraftState Draft { get; }
        public MemberBinding(AudioSettingsItemViewModel item, FieldDescriptor field, ConfigurationSectionEditor section, Action changed)
        {
            _item = item; Field = field; _section = section; _changed = changed;
            Draft = new(() => Read().Json?.GetRawText() ?? "<absent>"); Draft.Changed += changed;
        }
        public bool IsEnabled => !_item._released && _item._owner.IsEnabled && _item._owner.Contains(_item);
        public object HostContext => _section;
        public FieldValue Read() => new(_item.Value.TryGetProperty(Field.Id, out var value) ? value : null);
        public event EventHandler? Changed;
        public Task<ValueIssue?> CommitAsync(FieldValue value, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(IsEnabled ? _item._owner.Commit(_item, Field, value) : new ValueIssue("settings.obsolete", _item._owner.L["Settings.Project.Audio.Obsolete"]));
        }
        public void Refresh() => Changed?.Invoke(this, EventArgs.Empty);
        public void Release() { Draft.Changed -= _changed; Changed = null; }
    }
}
