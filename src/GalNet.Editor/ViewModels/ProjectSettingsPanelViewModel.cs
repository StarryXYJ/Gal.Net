using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Core.Settings;
using GalNet.Editor.Project;
using Serilog;

namespace GalNet.Editor.ViewModels;

/// <summary>
/// 项目设置面板 —— 编辑 ProjectSettings（分辨率、语言、存档槽位数等）。
/// </summary>
public partial class ProjectSettingsPanelViewModel : ObservableObject
{
    private readonly IProjectService _projectService;

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

    public ProjectSettingsPanelViewModel(IProjectService projectService)
    {
        _projectService = projectService;
        LoadFromProject();
    }

    private void LoadFromProject()
    {
        if (_projectService.Current?.Settings is not { } s) return;
        DefaultWidth = s.DefaultWidth;
        DefaultHeight = s.DefaultHeight;
        SaveSlotCount = s.SaveSlotCount;
        SfxChannelCount = s.SfxChannelCount;
        TargetLocale = s.TargetLocale.Code;
        IsDirtyText = _projectService.Current.IsDirty ? "（有未保存的修改）" : "";
    }

    /// <summary>保存项目设置到 settings.json</summary>
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

            IsDirtyText = "已保存";
            Log.Information("Project settings saved");
        }
        catch (Exception ex)
        {
            IsDirtyText = "保存失败";
            Log.Error(ex, "Failed to save project settings");
        }
    }
}
