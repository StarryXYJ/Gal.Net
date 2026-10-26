using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Core.Audio;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Project;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using GalNet.Editor.Services;
using GalNet.Editor.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace GalNet.Editor.ViewModels;

public partial class ProjectSettingsPanelViewModel : ObservableObject, IDisposable, IUndoRedoTarget
{
    public IReadOnlyList<string> ResolutionPresets { get; } = NewProjectPanelViewModel.ResolutionPresets;
    public IReadOnlyList<string> AudioBuses { get; } = [GalNet.Core.Audio.AudioBuses.Bgm, GalNet.Core.Audio.AudioBuses.Sfx, GalNet.Core.Audio.AudioBuses.Vocal];
    public ObservableCollection<AudioTrackEditorItem> AudioTracks { get; } = [];
    private readonly IProjectService _projectService;
    private readonly EditorHistories _histories;
    private bool _isLoading;
    private bool _hasDraft;
    private readonly IDisposable _draftRegistration;
    private readonly IProjectContentSession _session;
    public IUndoRedoHistory UndoRedoHistory => _histories.Content;

    public IEditorLocalizationService L { get; }
    public event Action? DraftReset;

    [ObservableProperty]
    private int _defaultWidth = 1920;

    [ObservableProperty]
    private int _defaultHeight = 1080;

    [ObservableProperty]
    private int _saveSlotCount = 60;


    [ObservableProperty]
    private string _targetLocale = "zh-CN";

    [ObservableProperty]
    private string _isDirtyText = "";

    [ObservableProperty]
    private string _resolution = "1920×1080";

    [ObservableProperty]
    private string _projectName = "";
    [ObservableProperty] private string _debugExecutable = "";
    [ObservableProperty] private string _debugArguments = "";
    [ObservableProperty] private string _debugWorkingDirectory = "";
    [ObservableProperty] private string _debugConfigurationMessage = "";

    [RelayCommand]
    private void SaveDebugConfiguration()
    {
        try
        {
            if (_projectService.Current is not { } project) return;
            ExternalGameDebugClient.SaveSettings(project.RootPath, new(DebugExecutable.Trim(),
                DebugArguments.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries),
                string.IsNullOrWhiteSpace(DebugWorkingDirectory) ? null : DebugWorkingDirectory.Trim()));
            DebugConfigurationMessage = "";
        }
        catch (Exception error) { DebugConfigurationMessage = error.Message; }
    }

    public ProjectSettingsPanelViewModel(
        IProjectService projectService,
        EditorHistories histories,
        IEditorLocalizationService localization,
        IProjectContentSession session)
    {
        _projectService = projectService;
        _histories = histories;
        _session = session;
        _draftRegistration = session.RegisterDraft(() => HasDraftChanges, CommitDraftAsync, LoadFromProject);
        L = localization;
        L.PropertyChanged += OnLocalizationChanged;

        _projectService.CurrentChanged += OnProjectChanged;
        _histories.Content.Changed += OnHistoryChanged;
        _session.ExternalReloaded += LoadFromProject;
        LoadFromProject();
    }

    public void Dispose()
    {
        _projectService.CurrentChanged -= OnProjectChanged;
        _draftRegistration.Dispose();
        _histories.Content.Changed -= OnHistoryChanged;
        _session.ExternalReloaded -= LoadFromProject;
        L.PropertyChanged -= OnLocalizationChanged;
        foreach (var track in AudioTracks) { track.Changed -= TriggerAutoSave; track.RemoveRequested = null; }
        GC.SuppressFinalize(this);
    }

    private void LoadFromProject()
    {
        if (_projectService.Current is not { } project) return;
        var settings = project.Settings;

        _isLoading = true;
        ProjectName = project.Name;
        try
        {
            if (ExternalGameDebugClient.LoadSettings(project.RootPath) is { } launch)
            {
                DebugExecutable = launch.Executable;
                DebugArguments = string.Join(Environment.NewLine, launch.Arguments ?? []);
                DebugWorkingDirectory = launch.WorkingDirectory ?? "";
            }
        }
        catch (Exception error) when (error is System.IO.IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { DebugConfigurationMessage = error.Message; }
        DefaultWidth = settings.DefaultWidth;
        DefaultHeight = settings.DefaultHeight;
        Resolution = $"{DefaultWidth}×{DefaultHeight}";
        SaveSlotCount = settings.SaveSlotCount;

        TargetLocale = settings.TargetLocale.Code;
        foreach (var track in AudioTracks) track.Changed -= TriggerAutoSave;
        AudioTracks.Clear();
        foreach (var track in settings.AudioTracks)
        {
            var item = AttachTrack(AudioTrackEditorItem.From(track));
            AudioTracks.Add(item);
        }
        _isLoading = false;
        _hasDraft = false;
        _validationError = null;
        UpdateDirtyText();
        DraftReset?.Invoke();
    }

    private Exception? _validationError;
    private void UpdateDirtyText()
    {
        IsDirtyText = _validationError is { } error ? L.Format("Settings.Project.ApplyFailed", EditorText.Error(error).Render(L))
            : _histories.Content.IsDirty ? L["Settings.Project.Unsaved"] : "";
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        try
        {
            if (_projectService.Current is not { } project) return;
            if (string.IsNullOrWhiteSpace(ProjectName)) throw new EditorOperationException("project.nameRequired", "Project name is required.");
            string locale;
            try { locale = new Core.I18n.I18nLocale(TargetLocale).Code; }
            catch (Exception failure) when (failure is ArgumentException or FormatException)
            { throw new EditorOperationException("settings.localeInvalid", failure.Message, TargetLocale); }
            if (locale != project.Settings.TargetLocale.Code)
                throw new EditorOperationException("i18n.development", L["I18n.UsePanel"]);
            var before = SettingsSnapshot.From(project.Name, project.Settings);
            var after = new SettingsSnapshot(ProjectName.Trim(), DefaultWidth, DefaultHeight, SaveSlotCount, locale, AudioTracks.Select(item => item.ToDefinition()).ToArray());
            ValidateTracks(after.AudioTracks);
            if (before.Matches(after)) { LoadFromProject(); return; }
            if (!NewProjectPanelViewModel.TryParseResolution($"{DefaultWidth}×{DefaultHeight}", out _, out _) || SaveSlotCount <= 0)
                throw new EditorOperationException("settings.invalid", "Invalid project dimensions or channel/slot count.");
            var graph = project.Services.GetRequiredService<IEditorDocumentService>().CurrentDocument;
            await _histories.Content.ExecuteAsync(new SettingsEdit(project.Settings, graph, before, after));
            LoadFromProject();
            UpdateDirtyText();
            Log.Information("Project settings updated");
        }
        catch (Exception ex)
        {
            _validationError = ex;
            UpdateDirtyText();
            Log.Error(ex, "Failed to save project settings");
        }
    }

    partial void OnDefaultWidthChanged(int value) => TriggerAutoSave();
    partial void OnProjectNameChanged(string value) => TriggerAutoSave();
    partial void OnDefaultHeightChanged(int value) => TriggerAutoSave();
    partial void OnResolutionChanged(string value)
    {
        if (_isLoading || !NewProjectPanelViewModel.TryParseResolution(value, out var width, out var height)) return;
        _isLoading = true; DefaultWidth = width; DefaultHeight = height; _isLoading = false; TriggerAutoSave();
    }
    partial void OnSaveSlotCountChanged(int value) => TriggerAutoSave();

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
        if (tracks.Count == 0) throw new EditorOperationException("settings.tracksRequired", "At least one audio track is required.");
        if (tracks.Any(track => string.IsNullOrWhiteSpace(track.Id)) || tracks.Select(track => track.Id).Distinct(StringComparer.Ordinal).Count() != tracks.Count)
            throw new EditorOperationException("settings.trackIds", "Audio track IDs must be non-empty and unique.");
        if (tracks.Any(track => !GalNet.Core.Audio.AudioBuses.IsValid(track.Bus) || !float.IsFinite(track.DefaultGain) || track.DefaultGain < 0 || track.DefaultGain > 1 || track.MaxVoices <= 0 || track.MaxQueuedItems < 0))
            throw new EditorOperationException("settings.trackInvalid", "Audio track configuration contains an invalid bus, gain, or voice limit.");
    }

    private void TriggerAutoSave()
    {
        if (_isLoading || _projectService.Current is null)
            return;
        _hasDraft = true;
        if (HasDraftChanges) IsDirtyText = L["Settings.Project.Unsaved"];
        else UpdateDirtyText();
        _session.Refresh();
    }

    private void OnProjectChanged(Abstraction.Project.GalProject? _) => LoadFromProject();

    private void OnHistoryChanged()
    {
        if (!_histories.Content.IsBusy && !HasDraftChanges) LoadFromProject();
    }

    private void OnLocalizationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == "Item[]") UpdateDirtyText();
    }

    [RelayCommand]
    private void CancelDraft() { LoadFromProject(); _session.Refresh(); }

    public async Task CommitDraftAsync()
    {
        if (!HasDraftChanges) return;
        await SaveSettingsAsync();
        if (_hasDraft) throw _validationError ?? new EditorOperationException("settings.invalid", "Invalid settings draft.");
    }

    private bool HasDraftChanges => _hasDraft && _projectService.Current is { } project &&
        !SettingsSnapshot.From(project.Name, project.Settings).Matches(new((ProjectName ?? "").Trim(), DefaultWidth, DefaultHeight, SaveSlotCount,
            TargetLocale, AudioTracks.Select(item => item.ToDefinition()).ToArray()));

    private sealed record SettingsEdit(Core.Settings.ProjectSettings Settings, EditorGraphDocument Graph, SettingsSnapshot Before, SettingsSnapshot After) : IUndoableEdit
    {
        public long RetainedBytes => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(Before).LongLength + System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(After).LongLength;
        public string Description => "Change project name and settings";
        public void Undo() => Apply(After, Before);
        public void Redo() => Apply(Before, After);
        private void Apply(SettingsSnapshot expected, SettingsSnapshot value)
        {
            if (!SettingsSnapshot.From(Graph.Name, Settings).Matches(expected))
                throw new EditorOperationException("settings.changed", "Project settings changed before this edit. Review the current values and retry.");
            value.Apply(Settings);
            Graph.Name = value.Name;
        }
    }

    private sealed record SettingsSnapshot(string Name, int Width, int Height, int SaveSlots, string Locale, IReadOnlyList<AudioTrackDefinition> AudioTracks)
    {
        public static SettingsSnapshot From(string name, Core.Settings.ProjectSettings settings) =>
            new(name, settings.DefaultWidth, settings.DefaultHeight, settings.SaveSlotCount, settings.TargetLocale.Code, settings.AudioTracks.Select(CopyTrack).ToArray());
        public bool Matches(SettingsSnapshot other) =>
            Name == other.Name && Width == other.Width && Height == other.Height && SaveSlots == other.SaveSlots && Locale == other.Locale &&
            AudioTracks.Count == other.AudioTracks.Count && AudioTracks.Zip(other.AudioTracks).All(pair => Same(pair.First, pair.Second));
        public void Apply(Core.Settings.ProjectSettings settings)
        {
            var locale = new Core.I18n.I18nLocale(Locale);
            var tracks = AudioTracks.Select(CopyTrack).ToList();
            settings.DefaultWidth = Width; settings.DefaultHeight = Height; settings.SaveSlotCount = SaveSlots;
            settings.TargetLocale = locale;
            settings.AudioTracks = tracks;
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
    [ObservableProperty] private AudioOverflowPolicy _overflowPolicy;
    [ObservableProperty] private AudioQueueOverflowPolicy _queueOverflowPolicy;

    public static AudioTrackEditorItem From(AudioTrackDefinition track) => new()
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
    public AudioTrackDefinition ToDefinition() => new()
    {
        Id = Id,
        Name = Name,
        Bus = Bus,
        DefaultGain = (float)DefaultGain,
        MaxVoices = MaxVoices,
        MaxQueuedItems = MaxQueuedItems,
        OverflowPolicy = OverflowPolicy,
        QueueOverflowPolicy = QueueOverflowPolicy
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
