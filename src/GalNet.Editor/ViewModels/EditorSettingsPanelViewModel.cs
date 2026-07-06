using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Editor.Services;
using Serilog;

namespace GalNet.Editor.ViewModels;

public partial class EditorSettingsPanelViewModel : ObservableObject
{
    private readonly IEditorSettingsService _editorSettings;
    private readonly IThemeRegistry _themeRegistry;
    private readonly IThemeService _themeService;

    [ObservableProperty]
    private string _uiLocale = "zh-CN";

    [ObservableProperty]
    private ThemeSelectionItem? _selectedTheme;

    [ObservableProperty]
    private string _statusText = "";

    public ObservableCollection<ThemeSelectionItem> AvailableThemes { get; } = [];

    public string[] AvailableLocales { get; } = ["zh-CN", "en-US", "ja-JP", "ko-KR"];

    public EditorSettingsPanelViewModel(
        IEditorSettingsService editorSettings,
        IThemeRegistry themeRegistry,
        IThemeService themeService)
    {
        _editorSettings = editorSettings;
        _themeRegistry = themeRegistry;
        _themeService = themeService;

        LoadAvailableThemes();
        LoadFromSettings();
    }

    private void LoadAvailableThemes()
    {
        AvailableThemes.Clear();
        foreach (var theme in _themeRegistry.GetAvailableThemes().Values)
            AvailableThemes.Add(new ThemeSelectionItem(theme.Name, theme.DisplayName));
    }

    private void LoadFromSettings()
    {
        var settings = _editorSettings.GetSettings();
        UiLocale = settings.UiLocale.Code;

        SelectedTheme = AvailableThemes.FirstOrDefault(theme =>
                            string.Equals(theme.Name, settings.Theme, StringComparison.OrdinalIgnoreCase))
                        ?? AvailableThemes.FirstOrDefault(theme => theme.Name == "Violet")
                        ?? AvailableThemes.FirstOrDefault();
    }

    partial void OnSelectedThemeChanged(ThemeSelectionItem? value)
    {
        if (value is null) return;

        var settings = _editorSettings.GetSettings();
        settings.Theme = value.Name;
        _themeService.ApplyThemeByName(value.Name);
        _editorSettings.SaveSettings();

        StatusText = $"已切换到 {value.DisplayName}";
    }

    [RelayCommand]
    private void SaveSettings()
    {
        try
        {
            var settings = _editorSettings.GetSettings();
            settings.UiLocale = new Core.I18n.I18nLocale(UiLocale);
            if (SelectedTheme is not null)
                settings.Theme = SelectedTheme.Name;

            _editorSettings.SaveSettings();

            StatusText = "已保存";
            Log.Information(
                "Editor settings saved: Theme={Theme}, Locale={Locale}",
                settings.Theme,
                UiLocale);
        }
        catch (Exception ex)
        {
            StatusText = "保存失败";
            Log.Error(ex, "Failed to save editor settings");
        }
    }
}

public sealed class ThemeSelectionItem
{
    public ThemeSelectionItem(string name, string displayName)
    {
        Name = name;
        DisplayName = displayName;
    }

    public string Name { get; }
    public string DisplayName { get; }
}
