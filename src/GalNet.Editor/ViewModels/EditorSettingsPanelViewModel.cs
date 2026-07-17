using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Core.I18n;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Services;
using GalNet.Editor.Commands;
using Serilog;

namespace GalNet.Editor.ViewModels;

public partial class EditorSettingsPanelViewModel : ObservableObject
{
    private readonly IEditorSettingsService _editorSettings;
    private readonly IThemeRegistry _themeRegistry;
    private readonly IThemeService _themeService;
    private readonly EditorShortcutService _shortcutService;
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
    public ObservableCollection<ShortcutSettingItemViewModel> Shortcuts { get; } = [];

    [ObservableProperty]
    private string _shortcutSearchText = "";

    public EditorSettingsPanelViewModel(
        IEditorSettingsService editorSettings,
        IThemeRegistry themeRegistry,
        IThemeService themeService,
        EditorShortcutService shortcutService,
        IEditorLocalizationService localization)
    {
        _editorSettings = editorSettings;
        _themeRegistry = themeRegistry;
        _themeService = themeService;
        _shortcutService = shortcutService;
        L = localization;

        L.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == "Item[]")
                ReloadDisplayNames();
        };
        

        LoadFromSettings();
        ReloadShortcuts();
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
        ReloadShortcuts();
    }

    partial void OnShortcutSearchTextChanged(string value) => ReloadShortcuts();

    private void ReloadShortcuts()
    {
        var search = ShortcutSearchText.Trim();
        Shortcuts.Clear();
        foreach (var command in _shortcutService.Commands.Where(command =>
                     string.IsNullOrEmpty(search) ||
                     command.Id.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                     L[command.DisplayNameKey.Key].Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                     L[command.CategoryKey.Key].Contains(search, StringComparison.CurrentCultureIgnoreCase)))
        {
            Shortcuts.Add(new ShortcutSettingItemViewModel(command, _shortcutService, L));
        }
    }

    [RelayCommand]
    private void ResetAllShortcuts()
    {
        _shortcutService.ResetAll();
        ReloadShortcuts();
        StatusText = L["Settings.Saved"];
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

public sealed partial class ShortcutSettingItemViewModel : ObservableObject
{
    private readonly IEditorShortcutCommandDefinition _definition;
    private readonly EditorShortcutService _service;

    public string CommandId => _definition.Id;
    public string DisplayName { get; }
    public string Category { get; }
    public string Context => _definition.Context;
    public string DefaultGestureText => _definition.DefaultGesture?.ToString() ?? "";

    [ObservableProperty]
    private string _gestureText;

    [ObservableProperty]
    private string _validationMessage = "";

    public ShortcutSettingItemViewModel(
        IEditorShortcutCommandDefinition definition,
        EditorShortcutService service,
        IEditorLocalizationService localization)
    {
        _definition = definition;
        _service = service;
        DisplayName = localization[definition.DisplayNameKey.Key];
        Category = localization[definition.CategoryKey.Key];
        _gestureText = definition.Gesture?.ToString() ?? "";
    }

    partial void OnGestureTextChanged(string value)
    {
        ValidationMessage = Validate(value);
    }

    [RelayCommand]
    private void Apply()
    {
        ValidationMessage = Validate(GestureText);
        if (!string.IsNullOrEmpty(ValidationMessage)) return;
        _service.SetGesture(
            CommandId,
            string.IsNullOrWhiteSpace(GestureText) ? null : KeyGesture.Parse(GestureText));
    }

    [RelayCommand]
    private void Reset()
    {
        _service.ResetGesture(CommandId);
        GestureText = _definition.Gesture?.ToString() ?? "";
        ValidationMessage = "";
    }

    private string Validate(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        try
        {
            var gesture = KeyGesture.Parse(value);
            var conflict = _service.FindConflict(CommandId, Context, gesture);
            return conflict is null ? "" : $"Conflicts with {conflict.Id}";
        }
        catch
        {
            return "Invalid shortcut";
        }
    }
}
