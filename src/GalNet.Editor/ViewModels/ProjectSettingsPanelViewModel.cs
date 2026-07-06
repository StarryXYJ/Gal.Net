using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Editor.Project;
using GalNet.Editor.Services;
using Serilog;

namespace GalNet.Editor.ViewModels;

public partial class ProjectSettingsPanelViewModel : ObservableObject
{
    private readonly IProjectService _projectService;

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

    public ProjectSettingsPanelViewModel(
        IProjectService projectService,
        IEditorLocalizationService localization)
    {
        _projectService = projectService;
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

        DefaultWidth = settings.DefaultWidth;
        DefaultHeight = settings.DefaultHeight;
        SaveSlotCount = settings.SaveSlotCount;
        SfxChannelCount = settings.SfxChannelCount;
        TargetLocale = settings.TargetLocale.Code;
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
            project.IsDirty = false;

            IsDirtyText = L["Settings.Saved"];
            Log.Information("Project settings saved");
        }
        catch (Exception ex)
        {
            IsDirtyText = L["Settings.SaveFailed"];
            Log.Error(ex, "Failed to save project settings");
        }
    }
}
