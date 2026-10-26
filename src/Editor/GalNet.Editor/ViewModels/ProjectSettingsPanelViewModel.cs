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
    public ObservableCollection<AudioGroupEditorItem> AudioGroups { get; } = [];
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
        foreach (var group in AudioGroups) { group.Changed -= TriggerAutoSave; group.RemoveRequested = null; }
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
        foreach (var group in AudioGroups) { group.Changed -= TriggerAutoSave; group.RemoveRequested = null; }
        AudioGroups.Clear();
        foreach (var group in settings.AudioGroups)
        {
            var item = new AudioGroupEditorItem { Id = group.Id, Name = group.Name, RemoveRequested = RemoveAudioGroup };
            item.Changed += TriggerAutoSave;
            AudioGroups.Add(item);
        }
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
            var after = new SettingsSnapshot(ProjectName.Trim(), DefaultWidth, DefaultHeight, SaveSlotCount, locale, AudioTracks.Select(item => item.ToDefinition()).ToArray(), AudioGroups.Select(item => item.ToDefinition()).ToArray());
            _ = new AudioConfiguration(after.AudioTracks, after.AudioGroups);
            if (before.Matches(after)) { LoadFromProject(); return; }
            if (!NewProjectPanelViewModel.TryParseResolution($"{DefaultWidth}×{DefaultHeight}", out _, out _) || SaveSlotCount <= 0)
                throw new EditorOperationException("settings.invalid", "Invalid project dimensions or save slot count.");
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

    private string NextTrackId()
    {
        var number = 1;
        while (AudioTracks.Any(track => track.Id == $"track_{number}")) number++;
        return $"track_{number}";
    }

    [RelayCommand]
    private void AddAudioTrack()
    {
        var item = new AudioTrackEditorItem
        {
            Id = NextTrackId(),
            Name = "New track",
            GroupId = "",
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

    [RelayCommand]
    private void AddAudioGroup()
    {
        var number = 1;
        while (AudioGroups.Any(group => group.Id == $"group_{number}")) number++;
        var item = new AudioGroupEditorItem { Id = $"group_{number}", Name = "New group", RemoveRequested = RemoveAudioGroup };
        item.Changed += TriggerAutoSave;
        AudioGroups.Add(item);
        TriggerAutoSave();
    }

    [RelayCommand]
    private void RemoveAudioGroup(AudioGroupEditorItem? item)
    {
        if (item is null || !AudioGroups.Remove(item)) return;
        item.Changed -= TriggerAutoSave;
        item.RemoveRequested = null;
        foreach (var track in AudioTracks.Where(track => track.GroupId == item.Id)) track.GroupId = "";
        TriggerAutoSave();
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
            TargetLocale, AudioTracks.Select(item => item.ToDefinition()).ToArray(), AudioGroups.Select(item => item.ToDefinition()).ToArray()));

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

    private sealed record SettingsSnapshot(string Name, int Width, int Height, int SaveSlots, string Locale,
        IReadOnlyList<AudioTrackDefinition> AudioTracks, IReadOnlyList<AudioGroupDefinition> AudioGroups)
    {
        public static SettingsSnapshot From(string name, Core.Settings.ProjectSettings settings) =>
            new(name, settings.DefaultWidth, settings.DefaultHeight, settings.SaveSlotCount, settings.TargetLocale.Code,
                settings.AudioTracks.ToArray(), settings.AudioGroups.ToArray());
        public bool Matches(SettingsSnapshot other) =>
            Name == other.Name && Width == other.Width && Height == other.Height && SaveSlots == other.SaveSlots && Locale == other.Locale &&
            AudioTracks.SequenceEqual(other.AudioTracks) && AudioGroups.SequenceEqual(other.AudioGroups);
        public void Apply(Core.Settings.ProjectSettings settings)
        {
            settings.DefaultWidth = Width; settings.DefaultHeight = Height; settings.SaveSlotCount = SaveSlots;
            settings.TargetLocale = new Core.I18n.I18nLocale(Locale);
            settings.AudioTracks = AudioTracks.ToList();
            settings.AudioGroups = AudioGroups.ToList();
        }
    }
}

public sealed partial class AudioGroupEditorItem : ObservableObject
{
    public event Action? Changed;
    public Action<AudioGroupEditorItem>? RemoveRequested { get; set; }
    [ObservableProperty] private string _id = "";
    [ObservableProperty] private string _name = "";
    public AudioGroupDefinition ToDefinition() => new() { Id = Id, Name = Name };
    [RelayCommand] private void Remove() => RemoveRequested?.Invoke(this);
    partial void OnIdChanged(string value) => Changed?.Invoke();
    partial void OnNameChanged(string value) => Changed?.Invoke();
}

public sealed partial class AudioTrackEditorItem : ObservableObject
{
    public event Action? Changed;
    public Action<AudioTrackEditorItem>? RemoveRequested { get; set; }
    [ObservableProperty] private string _id = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _groupId = "";
    [ObservableProperty] private bool _preserveDuringCatchUp;
    [ObservableProperty] private double _defaultGain = 1;
    [ObservableProperty] private int _maxVoices = 1;
    [ObservableProperty] private int _maxQueuedItems;
    [ObservableProperty] private AudioOverflowPolicy _overflowPolicy;
    [ObservableProperty] private AudioQueueOverflowPolicy _queueOverflowPolicy;

    public static AudioTrackEditorItem From(AudioTrackDefinition track) => new()
    {
        Id = track.Id,
        Name = track.Name,
        GroupId = track.GroupId ?? "",
        PreserveDuringCatchUp = track.PreserveDuringCatchUp,
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
        GroupId = string.IsNullOrWhiteSpace(GroupId) ? null : GroupId,
        PreserveDuringCatchUp = PreserveDuringCatchUp,
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
    partial void OnGroupIdChanged(string value) => Changed?.Invoke();
    partial void OnDefaultGainChanged(double value) => Changed?.Invoke();
    partial void OnMaxVoicesChanged(int value) => Changed?.Invoke();
    partial void OnMaxQueuedItemsChanged(int value) => Changed?.Invoke();
    partial void OnPreserveDuringCatchUpChanged(bool value) => Changed?.Invoke();
    partial void OnOverflowPolicyChanged(AudioOverflowPolicy value) => Changed?.Invoke();
    partial void OnQueueOverflowPolicyChanged(AudioQueueOverflowPolicy value) => Changed?.Invoke();
}
