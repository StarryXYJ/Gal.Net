using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Core.I18n;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Services;
using Serilog;

namespace GalNet.Editor.ViewModels;

public partial class EditorSettingsPanelViewModel : ObservableObject
{
    private readonly IEditorSettingsService _editorSettings;
    private readonly IThemeRegistry _themeRegistry;
    private readonly IThemeService _themeService;
    private bool _loading;

    public IEditorLocalizationService L { get; }

    [ObservableProperty]
    private ThemeSelectionItem? _selectedTheme;

    [ObservableProperty]
    private CultureInfo? _selectedCulture;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private bool _autoSaveProject = true;

    public ObservableCollection<ThemeSelectionItem> AvailableThemes { get; } = [];
    public ObservableCollection<CultureInfo> AvailableCultures { get; } = [];

    public EditorSettingsPanelViewModel(
        IEditorSettingsService editorSettings,
        IThemeRegistry themeRegistry,
        IThemeService themeService,
        IEditorLocalizationService localization)
    {
        _editorSettings = editorSettings;
        _themeRegistry = themeRegistry;
        _themeService = themeService;
        L = localization;

        L.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == "Item[]")
                ReloadDisplayNames();
        };
        

        LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        _loading = true;
        ReloadDisplayNames();

        var settings = _editorSettings.GetSettings();
        SelectedTheme = AvailableThemes.FirstOrDefault(theme =>
                            string.Equals(theme.Name, settings.Theme, StringComparison.OrdinalIgnoreCase))
                        ?? AvailableThemes.FirstOrDefault(theme => theme.Name == "Violet")
                        ?? AvailableThemes.FirstOrDefault();

        SelectedCulture = AvailableCultures.FirstOrDefault(culture =>
                              string.Equals(culture.Name, settings.UiLocale.Code, StringComparison.OrdinalIgnoreCase))
                          ?? AvailableCultures.FirstOrDefault(culture => culture.Name == "zh-CN")
                          ?? AvailableCultures.FirstOrDefault();
        AutoSaveProject = settings.AutoSaveProject;
        _loading = false;
    }

    private void ReloadDisplayNames()
    {
        var selectedThemeName = SelectedTheme?.Name;

        AvailableThemes.Clear();
        foreach (var theme in _themeRegistry.GetAvailableThemes().Values)
            AvailableThemes.Add(new ThemeSelectionItem(theme.Name, L[theme.DisplayKey]));

        if (AvailableCultures.Count == 0)
        {
            foreach (var culture in L.AvailableCultures)
                AvailableCultures.Add(culture);
        }

        SelectedTheme = AvailableThemes.FirstOrDefault(theme => theme.Name == selectedThemeName) ?? SelectedTheme;
    }

    partial void OnSelectedThemeChanged(ThemeSelectionItem? value)
    {
        if (_loading || value is null) return;

        var settings = _editorSettings.GetSettings();
        settings.Theme = value.Name;
        _themeService.ApplyThemeByName(value.Name);
        _editorSettings.SaveSettings();

        StatusText = L.Format("Settings.ThemeChanged", value.DisplayName);
    }

    partial void OnSelectedCultureChanged(CultureInfo? value)
    {
        if (_loading || value is null) return;
        if (L.CurrentCulture.Name == value.Name) return;

        var settings = _editorSettings.GetSettings();
        settings.UiLocale = new I18nLocale(value.Name);
        _editorSettings.SaveSettings();
        L.ApplyCulture(value);

        StatusText = L["Settings.Saved"];
    }

    partial void OnAutoSaveProjectChanged(bool value)
    {
        if (_loading) return;
        var settings = _editorSettings.GetSettings();
        settings.AutoSaveProject = value;
        _editorSettings.SaveSettings();
        StatusText = value ? "Auto-save enabled" : "Auto-save disabled";
    }

    [RelayCommand]
    private void SaveSettings()
    {
        try
        {
            var settings = _editorSettings.GetSettings();
            if (SelectedTheme is not null)
                settings.Theme = SelectedTheme.Name;
            if (SelectedCulture is not null)
                settings.UiLocale = new I18nLocale(SelectedCulture.Name);

            _editorSettings.SaveSettings();

            StatusText = L["Settings.Saved"];
            Log.Information("Editor settings saved: Theme={Theme}, Locale={Locale}", settings.Theme, settings.UiLocale);
        }
        catch (Exception ex)
        {
            StatusText = L["Settings.SaveFailed"];
            Log.Error(ex, "Failed to save editor settings");
        }
    }
}

public sealed record ThemeSelectionItem(string Name, string DisplayName);
