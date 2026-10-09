using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Core.Audio;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using Serilog;

namespace GalNet.Editor.ViewModels;

public partial class ProjectSettingsPanelViewModel : ObservableObject, IDisposable, IUndoRedoTarget
{
    public IReadOnlyList<string> ResolutionPresets => NewProjectPanelViewModel.ResolutionPresets;
    public IReadOnlyList<string> AudioBuses { get; } = [GalNet.Core.Audio.AudioBuses.Bgm, GalNet.Core.Audio.AudioBuses.Sfx, GalNet.Core.Audio.AudioBuses.Vocal];
    public ObservableCollection<AudioTrackEditorItem> AudioTracks { get; } = [];
    private readonly IProjectService _projectService;
    private readonly EditorHistories _histories;
    private readonly IEditorSettingsService _editorSettings;
    private bool _isLoading;
    public IUndoRedoHistory UndoRedoHistory => _histories.Settings;

    public IEditorLocalizationService L { get; }

    [ObservableProperty]
    private int _defaultWidth = 1920;

    [ObservableProperty]
    private int _defaultHeight = 1080;

    [ObservableProperty]
    private int _saveSlotCount = 60;

    [ObservableProperty]
    private int _sfxChannelCount = 4;

    [ObservableProperty]
    private string _targetLocale = "zh-CN";

    [ObservableProperty]
    private string _isDirtyText = "";

    [ObservableProperty]
    private string _resolution = "1920×1080";

    public ProjectSettingsPanelViewModel(
        IProjectService projectService,
        EditorHistories histories,
        IEditorSettingsService editorSettings,
        IEditorLocalizationService localization)
    {
        _projectService = projectService;
        _histories = histories;
        _editorSettings = editorSettings;
        L = localization;
        L.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == "Item[]")
                UpdateDirtyText();
        };

        _projectService.CurrentChanged += OnProjectChanged;
        _histories.Settings.Changed += UpdateDirtyText;
        LoadFromProject();
    }

    public void Dispose()
    {
        _projectService.CurrentChanged -= OnProjectChanged;
        _histories.Settings.Changed -= UpdateDirtyText;
    }

    private void LoadFromProject()
    {
        if (_projectService.Current is not { } project) return;
        var settings = project.Settings;

        _isLoading = true;
        DefaultWidth = settings.DefaultWidth;
        DefaultHeight = settings.DefaultHeight;
        Resolution = $"{DefaultWidth}×{DefaultHeight}";
        SaveSlotCount = settings.SaveSlotCount;
        SfxChannelCount = settings.SfxChannelCount;
        TargetLocale = settings.TargetLocale.Code;
        foreach (var track in AudioTracks) track.Changed -= TriggerAutoSave;
        AudioTracks.Clear();
        foreach (var track in settings.AudioTracks)
        {
            var item = AttachTrack(AudioTrackEditorItem.From(track));
            AudioTracks.Add(item);
        }
        _isLoading = false;
        UpdateDirtyText();
    }

    private void UpdateDirtyText()
    {
        IsDirtyText = _histories.Settings.IsDirty ? L["Settings.Project.Unsaved"] : "";
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        try
        {
            if (_projectService.Current is not { } project) return;
            var before = SettingsSnapshot.From(project.Settings);
            var after = new SettingsSnapshot(DefaultWidth, DefaultHeight, SaveSlotCount, SfxChannelCount, TargetLocale, AudioTracks.Select(item => item.ToDefinition()).ToArray());
            ValidateTracks(after.AudioTracks);
            if (before.Matches(after)) return;
            after.Apply(project.Settings);
            _histories.Settings.PushAlreadyApplied(new DelegateEdit("Change project settings",
                () => { before.Apply(project.Settings); LoadFromProject(); },
                () => { after.Apply(project.Settings); LoadFromProject(); }));
            project.IsDirty = true;
            UpdateDirtyText();
            Log.Information("Project settings updated");
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            IsDirtyText = L["Settings.SaveFailed"];
            Log.Error(ex, "Failed to save project settings");
        }
    }

    partial void OnDefaultWidthChanged(int value) => TriggerAutoSave();
    partial void OnDefaultHeightChanged(int value) => TriggerAutoSave();
    partial void OnResolutionChanged(string value)
    {
        if (_isLoading || !NewProjectPanelViewModel.TryParseResolution(value, out var width, out var height)) return;
        _isLoading = true; DefaultWidth = width; DefaultHeight = height; _isLoading = false; TriggerAutoSave();
    }
    partial void OnSaveSlotCountChanged(int value) => TriggerAutoSave();
    partial void OnSfxChannelCountChanged(int value) => TriggerAutoSave();
    partial void OnTargetLocaleChanged(string value) => TriggerAutoSave();

    [RelayCommand]
    private void AddAudioTrack()
    {
        var item = new AudioTrackEditorItem
        {
            Id = $"track_{AudioTracks.Count + 1}",
            Name = "New track",
            Bus = GalNet.Core.Audio.AudioBuses.Sfx,
            DefaultGain = 1,
            MaxVoices = 1,
            MaxQueuedItems = 0
        };
        AudioTracks.Add(AttachTrack(item));
        TriggerAutoSave();
    }

    [RelayCommand]
    private void RemoveAudioTrack(AudioTrackEditorItem? item)
    {
        if (item is null || !AudioTracks.Remove(item)) return;
        item.Changed -= TriggerAutoSave;
        TriggerAutoSave();
    }

    private AudioTrackEditorItem AttachTrack(AudioTrackEditorItem item)
    {
        item.Changed += TriggerAutoSave;
        item.RemoveRequested = RemoveAudioTrack;
        return item;
    }

    private static void ValidateTracks(IReadOnlyList<AudioTrackDefinition> tracks)
    {
        if (tracks.Count == 0) throw new InvalidDataException("At least one audio track is required.");
        if (tracks.Any(track => string.IsNullOrWhiteSpace(track.Id)) || tracks.Select(track => track.Id).Distinct(StringComparer.Ordinal).Count() != tracks.Count)
            throw new InvalidDataException("Audio track IDs must be non-empty and unique.");
        if (tracks.Any(track => !GalNet.Core.Audio.AudioBuses.IsValid(track.Bus) || !float.IsFinite(track.DefaultGain) || track.DefaultGain < 0 || track.DefaultGain > 1 || track.MaxVoices <= 0 || track.MaxQueuedItems < 0))
            throw new InvalidDataException("Audio track configuration contains an invalid bus, gain, or voice limit.");
    }

    private void TriggerAutoSave()
    {
        if (_isLoading || _projectService.Current is null)
            return;
        IsDirtyText = L["Settings.Project.Unsaved"];
    }

    private void OnProjectChanged(Abstraction.Project.GalProject? _) => LoadFromProject();

    private sealed record SettingsSnapshot(int Width, int Height, int SaveSlots, int SfxChannels, string Locale, IReadOnlyList<AudioTrackDefinition> AudioTracks)
    {
        public static SettingsSnapshot From(Core.Settings.ProjectSettings settings) =>
            new(settings.DefaultWidth, settings.DefaultHeight, settings.SaveSlotCount, settings.SfxChannelCount, settings.TargetLocale.Code, settings.AudioTracks.Select(CopyTrack).ToArray());
        public bool Matches(SettingsSnapshot other) =>
            Width == other.Width && Height == other.Height && SaveSlots == other.SaveSlots && SfxChannels == other.SfxChannels && Locale == other.Locale &&
            AudioTracks.Count == other.AudioTracks.Count && AudioTracks.Zip(other.AudioTracks).All(pair => Same(pair.First, pair.Second));
        public void Apply(Core.Settings.ProjectSettings settings)
        {
            settings.DefaultWidth = Width; settings.DefaultHeight = Height; settings.SaveSlotCount = SaveSlots;
            settings.SfxChannelCount = SfxChannels; settings.TargetLocale = new Core.I18n.I18nLocale(Locale);
            settings.AudioTracks = AudioTracks.Select(CopyTrack).ToList();
        }

        private static bool Same(AudioTrackDefinition left, AudioTrackDefinition right) =>
            left.Id == right.Id && left.Name == right.Name && left.Bus == right.Bus && left.DefaultGain == right.DefaultGain &&
            left.MaxVoices == right.MaxVoices && left.MaxQueuedItems == right.MaxQueuedItems &&
            left.OverflowPolicy == right.OverflowPolicy && left.QueueOverflowPolicy == right.QueueOverflowPolicy;
        private static AudioTrackDefinition CopyTrack(AudioTrackDefinition track) => new()
        {
            Id = track.Id,
            Name = track.Name,
            Bus = track.Bus,
            DefaultGain = track.DefaultGain,
            MaxVoices = track.MaxVoices,
            MaxQueuedItems = track.MaxQueuedItems,
            OverflowPolicy = track.OverflowPolicy,
            QueueOverflowPolicy = track.QueueOverflowPolicy
        };
    }
}

public sealed partial class AudioTrackEditorItem : ObservableObject
{
    public event Action? Changed;
    public Action<AudioTrackEditorItem>? RemoveRequested { get; set; }
    [ObservableProperty] private string _id = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _bus = AudioBuses.Sfx;
    [ObservableProperty] private double _defaultGain = 1;
    [ObservableProperty] private int _maxVoices = 1;
    [ObservableProperty] private int _maxQueuedItems;

    public static AudioTrackEditorItem From(AudioTrackDefinition track) => new()
    {
        Id = track.Id,
        Name = track.Name,
        Bus = track.Bus,
        DefaultGain = track.DefaultGain,
        MaxVoices = track.MaxVoices,
        MaxQueuedItems = track.MaxQueuedItems
    };
    public AudioTrackDefinition ToDefinition() => new()
    {
        Id = Id,
        Name = Name,
        Bus = Bus,
        DefaultGain = (float)DefaultGain,
        MaxVoices = MaxVoices,
        MaxQueuedItems = MaxQueuedItems,
        OverflowPolicy = AudioOverflowPolicy.StopOldest,
        QueueOverflowPolicy = AudioQueueOverflowPolicy.DropNewest
    };

    [RelayCommand]
    private void Remove() => RemoveRequested?.Invoke(this);

    partial void OnIdChanged(string value) => Changed?.Invoke();
    partial void OnNameChanged(string value) => Changed?.Invoke();
    partial void OnBusChanged(string value) => Changed?.Invoke();
    partial void OnDefaultGainChanged(double value) => Changed?.Invoke();
    partial void OnMaxVoicesChanged(int value) => Changed?.Invoke();
    partial void OnMaxQueuedItemsChanged(int value) => Changed?.Invoke();
}
