using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using Serilog;

namespace GalNet.Editor.ViewModels;

public partial class ProjectSettingsPanelViewModel : ObservableObject, IDisposable, IUndoRedoTarget
{
    public IReadOnlyList<string> ResolutionPresets => NewProjectPanelViewModel.ResolutionPresets;
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
            var after = new SettingsSnapshot(DefaultWidth, DefaultHeight, SaveSlotCount, SfxChannelCount, TargetLocale);
            if (before == after) return;
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

    private void TriggerAutoSave()
    {
        if (_isLoading || _projectService.Current is null)
            return;
        IsDirtyText = L["Settings.Project.Unsaved"];
    }

    private void OnProjectChanged(Abstraction.Project.GalProject? _) => LoadFromProject();

    private sealed record SettingsSnapshot(int Width, int Height, int SaveSlots, int SfxChannels, string Locale)
    {
        public static SettingsSnapshot From(Core.Settings.ProjectSettings settings) =>
            new(settings.DefaultWidth, settings.DefaultHeight, settings.SaveSlotCount, settings.SfxChannelCount, settings.TargetLocale.Code);
        public void Apply(Core.Settings.ProjectSettings settings)
        {
            settings.DefaultWidth = Width; settings.DefaultHeight = Height; settings.SaveSlotCount = SaveSlots;
            settings.SfxChannelCount = SfxChannels; settings.TargetLocale = new Core.I18n.I18nLocale(Locale);
        }
    }
}
