using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Editor.Abstraction.Services;
using Serilog;

namespace GalNet.Editor.ViewModels;

public partial class ProjectSettingsPanelViewModel : ObservableObject
{
    public IReadOnlyList<string> ResolutionPresets => NewProjectPanelViewModel.ResolutionPresets;
    private readonly IProjectService _projectService;
    private readonly IEditorSettingsService _editorSettings;
    private bool _isLoading;

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
        IEditorSettingsService editorSettings,
        IEditorLocalizationService localization)
    {
        _projectService = projectService;
        _editorSettings = editorSettings;
        L = localization;
        L.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == "Item[]")
                UpdateDirtyText();
        };

        LoadFromProject();
    }

    private void LoadFromProject()
    {
        if (_projectService.Current?.Settings is not { } settings) return;

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
        IsDirtyText = _projectService.Current?.IsDirty == true ? L["Settings.Project.Unsaved"] : "";
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        try
        {
            if (_projectService.Current is not { } project) return;

            project.Settings.DefaultWidth = DefaultWidth;
            project.Settings.DefaultHeight = DefaultHeight;
            project.Settings.SaveSlotCount = SaveSlotCount;
            project.Settings.SfxChannelCount = SfxChannelCount;
            project.Settings.TargetLocale = new Core.I18n.I18nLocale(TargetLocale);

            await _projectService.SaveAsync();
            IsDirtyText = L["Settings.Saved"];
            Log.Information("Project settings saved");
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
        _ = SaveSettingsAsync();
    }
}
