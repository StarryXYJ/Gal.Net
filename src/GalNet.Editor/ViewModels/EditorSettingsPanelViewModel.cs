using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Editor.Services;
using Serilog;

namespace GalNet.Editor.ViewModels;

/// <summary>
/// 编辑器设置面板 —— 编辑 EditorSettings（主题、语言等）。
/// </summary>
public partial class EditorSettingsPanelViewModel : ObservableObject
{
    private readonly IEditorSettingsService _editorSettings;

    [ObservableProperty]
    private string _uiLocale = "zh-CN";

    [ObservableProperty]
    private string _theme = "Dark";

    [ObservableProperty]
    private string _statusText = "";

    /// <summary>可用主题列表</summary>
    public string[] AvailableThemes { get; } = ["Dark", "Light"];

    /// <summary>可用语言列表</summary>
    public string[] AvailableLocales { get; } = ["zh-CN", "en-US", "ja-JP", "ko-KR"];

    public EditorSettingsPanelViewModel(IEditorSettingsService editorSettings)
    {
        _editorSettings = editorSettings;
        LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        var s = _editorSettings.GetSettings();
        UiLocale = s.UiLocale.Code;
        Theme = s.Theme;
    }

    [RelayCommand]
    private void SaveSettings()
    {
        try
        {
            var s = _editorSettings.GetSettings();
            s.UiLocale = new Core.I18n.I18nLocale(UiLocale);
            s.Theme = Theme;
            _editorSettings.SaveSettings();

            StatusText = "已保存";
            Log.Information("Editor settings saved: Theme={Theme}, Locale={Locale}", Theme, UiLocale);
        }
        catch (Exception ex)
        {
            StatusText = "保存失败";
            Log.Error(ex, "Failed to save editor settings");
        }
    }
}
