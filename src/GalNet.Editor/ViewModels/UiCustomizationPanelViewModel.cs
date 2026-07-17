using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Control.Abstraction.UI;
using GalNet.Core.Assets;
using GalNet.Core.UI;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Abstraction.Changes;
using GalNet.Editor.History;
using Serilog;

namespace GalNet.Editor.ViewModels;

/// <summary>Generic editor for preset-owned settings. Display strings remain localization keys until XAML resolves them.</summary>
public sealed partial class UiCustomizationPanelViewModel : ObservableObject, IDisposable, IUndoRedoTarget
{
    private readonly IProjectService _projectService;
    private readonly EditorHistories _histories;
    private readonly EditorWorkspaceViewModel _workspace;
    private readonly IUiPresetRegistry _presets;
    private readonly IAssetManager _assets;
    private UiProject? _appliedSnapshot;
    public IUndoRedoHistory UndoRedoHistory => _histories.Ui;

    [ObservableProperty]
    private string _statusKey = "UiPreset.Status.NoProject";

    public ObservableCollection<UiPageEditorViewModel> Pages { get; } = [];
    public string CurrentColorPaletteId => _projectService.Current?.UiProject.Current.ColorPaletteId ?? UiColorPalettePresets.DefaultId;

    public UiCustomizationPanelViewModel(IProjectService projectService, EditorHistories histories, EditorWorkspaceViewModel workspace, IUiPresetRegistry presets, IAssetManager assets)
    {
        _projectService = projectService;
        _histories = histories;
        _workspace = workspace;
        _presets = presets;
        _assets = assets;
        _projectService.CurrentChanged += OnProjectChanged;
        LoadProject();
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (_projectService.Current is not { } project)
            return;

        try
        {
            var ui = project.UiProject.Current;
            var title = ui.GetPage(UiPageKind.Title);
            Log.Information("Saving UI customization for {Project}: titlePreset={TitlePreset}, titleValuesBefore={@TitleValues}",
                project.Name, title.PresetId, title.Settings);
            project.UiProject.NotifyChanged();
            var before = _appliedSnapshot is null ? EditorDocumentCloner.CloneUiProject(ui) : EditorDocumentCloner.CloneUiProject(_appliedSnapshot);
            var after = EditorDocumentCloner.CloneUiProject(ui);
            _histories.Ui.PushAlreadyApplied(new DelegateEdit("Apply UI customization",
                () => { project.UiProject.Replace(EditorDocumentCloner.CloneUiProject(before)); LoadProject(); },
                () => { project.UiProject.Replace(EditorDocumentCloner.CloneUiProject(after)); LoadProject(); }));
            _appliedSnapshot = EditorDocumentCloner.CloneUiProject(after);
            project.IsDirty = true;
            await _workspace.SaveAsync();

            if (_workspace.ActivePreview is { } preview)
                await preview.RestartAsync();
            Log.Information("UI customization saved and preview refreshed for {Project}", project.Name);
            StatusKey = "UiPreset.Status.Applied";
        }
        catch (Exception ex)
        {
            StatusKey = "UiPreset.Status.SaveFailed";
            Log.Error(ex, "Failed to save UI preset configuration");
        }
    }

    public async Task ApplyColorPaletteAsync(string paletteId)
    {
        if (_projectService.Current is not { } project)
            return;

        try
        {
            var before = EditorDocumentCloner.CloneUiProject(project.UiProject.Current);
            UiColorPalettePresets.Apply(project.UiProject.Current, paletteId);
            project.UiProject.NotifyChanged();
            var after = EditorDocumentCloner.CloneUiProject(project.UiProject.Current);
            _histories.Ui.PushAlreadyApplied(new DelegateEdit("Apply UI color palette",
                () => { project.UiProject.Replace(EditorDocumentCloner.CloneUiProject(before)); LoadProject(); },
                () => { project.UiProject.Replace(EditorDocumentCloner.CloneUiProject(after)); LoadProject(); }));
            _appliedSnapshot = EditorDocumentCloner.CloneUiProject(after);
            project.IsDirty = true;
            await _workspace.SaveAsync();
            if (_workspace.ActivePreview is { } preview)
                await preview.RestartAsync();
            LoadProject();
            OnPropertyChanged(nameof(CurrentColorPaletteId));
            StatusKey = "UiPreset.Status.Applied";
        }
        catch (Exception ex)
        {
            StatusKey = "UiPreset.Status.SaveFailed";
            Log.Error(ex, "Failed to reset UI color palette");
        }
    }

    #if false // Legacy typed UiProject synchronization; settings are now the sole source of truth.
    /// <summary>
    /// Syncs preset setting values (from UiPageSelection.Settings) to the
    /// top-level typed config properties (Title.BackgroundImage etc.)
    /// so that persisted JSON has consistent data regardless of which code
    /// path reads it.
    /// </summary>
    private static void SyncPresetSettingsToConfig(UiProject ui)
    {
        SyncTitleToConfig(ui.Title, ui.GetPage(UiPageKind.Title));
        SyncGameToConfig(ui.Game, ui.GetPage(UiPageKind.Game));
        SyncSettingsToConfig(ui.Settings, ui.GetPage(UiPageKind.Settings), includeSettingsControls: true);
        SyncSettingsToConfig(ui.SaveLoad, ui.GetPage(UiPageKind.SaveLoad), includeSettingsControls: false);
        SyncSettingsToConfig(ui.Gallery, ui.GetPage(UiPageKind.Gallery), includeSettingsControls: false);
        SyncAboutToConfig(ui.About, ui.GetPage(UiPageKind.About));
    }

    private static void SyncTitleToConfig(TitleUiConfiguration config, UiPageSelection settings)
    {
        ApplyText(settings, "backgroundImage", value => config.BackgroundImage = string.IsNullOrWhiteSpace(value) ? null : value);
        ApplyText(settings, "backgroundStretch", value => config.BackgroundStretch = value);
        ApplyColor(settings, "backgroundColor", value => config.BackgroundColor = value);
        ApplyColor(settings, "titleColor", value => config.TitleColor = value);
        ApplyColor(settings, "menuTextColor", value => config.MenuTextColor = value);
        ApplyColor(settings, "menuHoverTextColor", value => config.MenuHoverTextColor = value);
        ApplyColor(settings, "menuItemBackgroundColor", value => config.ButtonColor = value);
        ApplyColor(settings, "menuItemHoverBackgroundColor", value => config.ButtonHoverColor = value);
        ApplyNumber(settings, "contentPadding", value => config.ContentPadding = value);
        ApplyNumber(settings, "titleFontSize", value => config.TitleFontSize = value);
        ApplyNumber(settings, "menuFontSize", value => config.MenuFontSize = value);
        ApplyNumber(settings, "titleMenuGap", value => config.TitleMenuGap = value);
        ApplyNumber(settings, "menuSpacing", value => config.MenuSpacing = value);
        ApplyNumber(settings, "menuItemWidth", value => config.ButtonWidth = value);
        ApplyNumber(settings, "menuItemHeight", value => config.ButtonHeight = value);
        ApplyBoolean(settings, "showGallery", value => config.ShowGallery = value);
        ApplyBoolean(settings, "showAbout", value => config.ShowAbout = value);
    }

    private static void SyncGameToConfig(GameUiConfiguration config, UiPageSelection settings)
    {
        ApplyText(settings, "choiceLayout", value => config.ChoiceLayout = value);
        ApplyText(settings, "dialogueBackgroundImage", value => config.DialogueBackgroundImage = string.IsNullOrWhiteSpace(value) ? null : value);
        ApplyColor(settings, "dialogueBackgroundColor", value => config.DialogueBackgroundColor = value);
        ApplyColor(settings, "dialogueTextColor", value => config.DialogueTextColor = value);
        ApplyColor(settings, "speakerTextColor", value => config.SpeakerTextColor = value);
        ApplyColor(settings, "choiceButtonColor", value => config.ChoiceButtonColor = value);
        ApplyColor(settings, "choiceButtonTextColor", value => config.ChoiceButtonTextColor = value);
        ApplyColor(settings, "commandTextColor", value => config.CommandTextColor = value);
        ApplyColor(settings, "commandHoverTextColor", value => config.CommandHoverTextColor = value);
        ApplyColor(settings, "commandSelectedTextColor", value => config.CommandSelectedTextColor = value);
        ApplyNumber(settings, "dialogueHeight", value => config.DialogueHeight = value);
        ApplyNumber(settings, "dialogueBackgroundImageOpacity", value => config.DialogueBackgroundImageOpacity = value);
        ApplyNumber(settings, "dialogueMargin", value => config.DialogueMargin = value);
        ApplyNumber(settings, "dialogueCornerRadius", value => config.DialogueCornerRadius = value);
        ApplyNumber(settings, "dialogueFontSize", value => config.DialogueFontSize = value);
        ApplyNumber(settings, "choiceButtonWidth", value => config.ChoiceButtonWidth = value);
        ApplyNumber(settings, "choiceButtonHeight", value => config.ChoiceButtonHeight = value);
        ApplyNumber(settings, "choiceSpacing", value => config.ChoiceSpacing = value);
        ApplyBoolean(settings, "commandBarVisible", value => config.CommandBarVisible = value);
    }

    private static void SyncSettingsToConfig(SettingsUiConfiguration config, UiPageSelection settings, bool includeSettingsControls)
    {
        ApplyColor(settings, "backgroundColor", value => config.BackgroundColor = value);
        ApplyColor(settings, "panelColor", value => config.PanelColor = value);
        ApplyColor(settings, "textColor", value => config.TextColor = value);
        ApplyColor(settings, "buttonColor", value => config.ButtonColor = value);
        ApplyColor(settings, "buttonTextColor", value => config.ButtonTextColor = value);
        ApplyColor(settings, "backButtonForegroundColor", value => config.BackButtonForegroundColor = value);
        if (!includeSettingsControls)
            return;
        ApplyColor(settings, "sliderTrackColor", value => config.SliderTrackColor = value);
        ApplyColor(settings, "sliderFillColor", value => config.SliderFillColor = value);
        ApplyColor(settings, "sliderThumbColor", value => config.SliderThumbColor = value);
        ApplyColor(settings, "sliderThumbBorderColor", value => config.SliderThumbBorderColor = value);
        ApplyColor(settings, "checkBoxBorderColor", value => config.CheckBoxBorderColor = value);
        ApplyColor(settings, "checkBoxFillColor", value => config.CheckBoxFillColor = value);
        ApplyColor(settings, "checkBoxCheckColor", value => config.CheckBoxCheckColor = value);
    }

    private static void SyncAboutToConfig(AboutUiConfiguration config, UiPageSelection settings)
    {
        ApplyText(settings, "contentAsset", value => config.ContentAsset = string.IsNullOrWhiteSpace(value) ? null : value);
        ApplyNumber(settings, "contentPadding", value => config.ContentPadding = value);
        ApplyNumber(settings, "fontSize", value => config.FontSize = value);
        ApplyNumber(settings, "codeFontSize", value => config.CodeFontSize = value);
        ApplyColor(settings, "backgroundColor", value => config.BackgroundColor = value);
        ApplyColor(settings, "panelColor", value => config.PanelColor = value);
        ApplyColor(settings, "textColor", value => config.TextColor = value);
        ApplyColor(settings, "headingColor", value => config.HeadingColor = value);
        ApplyColor(settings, "selectionColor", value => config.SelectionColor = value);
        ApplyColor(settings, "linkColor", value => config.LinkColor = value);
        ApplyColor(settings, "linkHoverColor", value => config.LinkHoverColor = value);
        ApplyColor(settings, "linkVisitedColor", value => config.LinkVisitedColor = value);
        ApplyColor(settings, "blockquoteBackgroundColor", value => config.BlockquoteBackgroundColor = value);
        ApplyColor(settings, "blockquoteBorderColor", value => config.BlockquoteBorderColor = value);
        ApplyColor(settings, "codeBackgroundColor", value => config.CodeBackgroundColor = value);
        ApplyColor(settings, "codeBorderColor", value => config.CodeBorderColor = value);
        ApplyColor(settings, "codeTextColor", value => config.CodeTextColor = value);
        ApplyColor(settings, "ruleColor", value => config.RuleColor = value);
        ApplyColor(settings, "backButtonForegroundColor", value => config.BackButtonForegroundColor = value);
    }

    private static void ApplyText(UiPageSelection settings, string key, Action<string> apply)
    {
        if (settings.Settings.TryGetValue(key, out var value) && value is not null)
            apply(value);
    }

    private static void ApplyColor(UiPageSelection settings, string key, Action<Color> apply)
    {
        if (settings.Settings.TryGetValue(key, out var value) && Color.TryParse(value, out var color))
            apply(color);
    }

    private static void ApplyNumber(UiPageSelection settings, string key, Action<double> apply)
    {
        if (settings.Settings.TryGetValue(key, out var value) && double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
            apply(number);
    }

    private static void ApplyBoolean(UiPageSelection settings, string key, Action<bool> apply)
    {
        if (settings.Settings.TryGetValue(key, out var value) && bool.TryParse(value, out var boolean))
            apply(boolean);
    }

    #endif

    private void OnProjectChanged(Abstraction.Project.GalProject? _) => LoadProject();

    private void LoadProject()
    {
        Pages.Clear();
        if (_projectService.Current is not { } project)
        {
            StatusKey = "UiPreset.Status.NoProject";
            return;
        }
        var ui = project.UiProject.Current;
        _appliedSnapshot = EditorDocumentCloner.CloneUiProject(ui);
        var title = ui.GetPage(UiPageKind.Title);
        Log.Information("Loading UI customization for {Project}: titlePreset={TitlePreset}, titleSettings={TitleSettings}, titleValues={@TitleValues}",
            project.Name, title.PresetId, title.Settings.Count, title.Settings);
        foreach (var page in Enum.GetValues<UiPageKind>())
            Pages.Add(new UiPageEditorViewModel(page, ui.GetPage(page), _presets, _assets));
        Log.Information("UI customization hydrated for {Project}: titlePreset={TitlePreset}, titleSettings={TitleSettings}, pageEditors={PageCount}",
            project.Name, title.PresetId, title.Settings.Count, Pages.Count);
        StatusKey = "UiPreset.Status.Ready";
    }

    #if false // Legacy typed UiProject synchronization; settings are now the sole source of truth.
    /// <summary>
    /// Syncs top-level typed config property values (Title.BackgroundImage etc.)
    /// into preset UiPageSelection.Settings so the panel displays them correctly
    /// even when the settings dictionary was not previously populated.
    /// </summary>
    private static void SyncConfigToPresetSettings(UiProject ui)
    {
        var titleSelection = ui.GetPage(UiPageKind.Title);
        SetMissing(titleSelection,
            ("backgroundImage", ui.Title.BackgroundImage ?? string.Empty),
            ("backgroundColor", Format(ui.Title.BackgroundColor)),
            ("backgroundStretch", ui.Title.BackgroundStretch),
            ("contentPadding", Format(ui.Title.ContentPadding)),
            ("titleColor", Format(ui.Title.TitleColor)),
            ("titleFontSize", Format(ui.Title.TitleFontSize)),
            ("menuTextColor", Format(ui.Title.MenuTextColor)),
            ("menuHoverTextColor", Format(ui.Title.MenuHoverTextColor)),
            ("menuFontSize", Format(ui.Title.MenuFontSize)),
            ("titleMenuGap", Format(ui.Title.TitleMenuGap)),
            ("menuSpacing", Format(ui.Title.MenuSpacing)),
            ("showGallery", Format(ui.Title.ShowGallery)),
            ("showAbout", Format(ui.Title.ShowAbout)),
            ("menuItemBackgroundColor", Format(ui.Title.ButtonColor)),
            ("menuItemHoverBackgroundColor", Format(ui.Title.ButtonHoverColor)),
            ("menuItemWidth", Format(ui.Title.ButtonWidth)),
            ("menuItemHeight", Format(ui.Title.ButtonHeight)));

        var gameSelection = ui.GetPage(UiPageKind.Game);
        SetMissing(gameSelection,
            ("dialogueBackgroundColor", Format(ui.Game.DialogueBackgroundColor)),
            ("dialogueBackgroundImage", ui.Game.DialogueBackgroundImage ?? string.Empty),
            ("dialogueBackgroundImageOpacity", Format(ui.Game.DialogueBackgroundImageOpacity)),
            ("dialogueTextColor", Format(ui.Game.DialogueTextColor)),
            ("speakerTextColor", Format(ui.Game.SpeakerTextColor)),
            ("dialogueHeight", Format(ui.Game.DialogueHeight)),
            ("dialogueMargin", Format(ui.Game.DialogueMargin)),
            ("dialogueCornerRadius", Format(ui.Game.DialogueCornerRadius)),
            ("dialogueFontSize", Format(ui.Game.DialogueFontSize)),
            ("choiceLayout", ui.Game.ChoiceLayout),
            ("choiceButtonColor", Format(ui.Game.ChoiceButtonColor)),
            ("choiceButtonTextColor", Format(ui.Game.ChoiceButtonTextColor)),
            ("choiceButtonWidth", Format(ui.Game.ChoiceButtonWidth)),
            ("choiceButtonHeight", Format(ui.Game.ChoiceButtonHeight)),
            ("choiceSpacing", Format(ui.Game.ChoiceSpacing)),
            ("commandBarVisible", Format(ui.Game.CommandBarVisible)),
            ("commandTextColor", Format(ui.Game.CommandTextColor)),
            ("commandHoverTextColor", Format(ui.Game.CommandHoverTextColor)),
            ("commandSelectedTextColor", Format(ui.Game.CommandSelectedTextColor)));

        SyncConfigToPresetSettings(ui.GetPage(UiPageKind.Settings), ui.Settings, includeSettingsControls: true);
        SyncConfigToPresetSettings(ui.GetPage(UiPageKind.SaveLoad), ui.SaveLoad, includeSettingsControls: false);
        SyncConfigToPresetSettings(ui.GetPage(UiPageKind.Gallery), ui.Gallery, includeSettingsControls: false);
        var about = ui.About;
        SetMissing(ui.GetPage(UiPageKind.About),
            ("contentAsset", about.ContentAsset ?? string.Empty),
            ("backgroundColor", Format(about.BackgroundColor)),
            ("panelColor", Format(about.PanelColor)),
            ("contentPadding", Format(about.ContentPadding)),
            ("fontSize", Format(about.FontSize)),
            ("textColor", Format(about.TextColor)),
            ("headingColor", Format(about.HeadingColor)),
            ("selectionColor", Format(about.SelectionColor)),
            ("linkColor", Format(about.LinkColor)),
            ("linkHoverColor", Format(about.LinkHoverColor)),
            ("linkVisitedColor", Format(about.LinkVisitedColor)),
            ("blockquoteBackgroundColor", Format(about.BlockquoteBackgroundColor)),
            ("blockquoteBorderColor", Format(about.BlockquoteBorderColor)),
            ("codeBackgroundColor", Format(about.CodeBackgroundColor)),
            ("codeBorderColor", Format(about.CodeBorderColor)),
            ("codeTextColor", Format(about.CodeTextColor)),
            ("codeFontSize", Format(about.CodeFontSize)),
            ("ruleColor", Format(about.RuleColor)),
            ("backButtonForegroundColor", Format(about.BackButtonForegroundColor)));
    }

    private static void SyncConfigToPresetSettings(UiPageSelection selection, SettingsUiConfiguration config, bool includeSettingsControls)
    {
        SetMissing(selection,
            ("backgroundColor", Format(config.BackgroundColor)),
            ("panelColor", Format(config.PanelColor)),
            ("textColor", Format(config.TextColor)),
            ("buttonColor", Format(config.ButtonColor)),
            ("buttonTextColor", Format(config.ButtonTextColor)),
            ("backButtonForegroundColor", Format(config.BackButtonForegroundColor)));
        if (!includeSettingsControls)
            return;
        SetMissing(selection,
            ("sliderTrackColor", Format(config.SliderTrackColor)),
            ("sliderFillColor", Format(config.SliderFillColor)),
            ("sliderThumbColor", Format(config.SliderThumbColor)),
            ("sliderThumbBorderColor", Format(config.SliderThumbBorderColor)),
            ("checkBoxBorderColor", Format(config.CheckBoxBorderColor)),
            ("checkBoxFillColor", Format(config.CheckBoxFillColor)),
            ("checkBoxCheckColor", Format(config.CheckBoxCheckColor)));
    }

    private static void SetMissing(UiPageSelection selection, params (string Key, string Value)[] values)
    {
        foreach (var (key, value) in values)
        {
            if (!selection.Settings.ContainsKey(key))
                selection.Settings[key] = value;
        }
    }

    private static string Format(Color value) => value.ToString();
    private static string Format(double value) => value.ToString("G", CultureInfo.InvariantCulture);
    private static string Format(bool value) => value.ToString().ToLowerInvariant();

    #endif

    public void Dispose()
    {
        _projectService.CurrentChanged -= OnProjectChanged;
    }
}

public sealed partial class UiPageEditorViewModel : ObservableObject
{
    private readonly UiPageSelection _selection;
    private readonly IUiPresetRegistry _presets;
    private readonly IAssetManager _assets;
    public string DisplayNameKey { get; }
    public ObservableCollection<UiPresetMetadata> Presets { get; }
    public ObservableCollection<UiSettingEditorViewModel> Settings { get; } = [];

    public UiPageEditorViewModel(UiPageKind page, UiPageSelection selection, IUiPresetRegistry presets, IAssetManager assets)
    {
        _selection = selection; _presets = presets; _assets = assets;
        DisplayNameKey = page switch { UiPageKind.Title => "UiPreset.Page.Title", UiPageKind.Game => "UiPreset.Page.Game", UiPageKind.Settings => "UiPreset.Page.Settings", UiPageKind.SaveLoad => "UiPreset.Page.SaveLoad", UiPageKind.Gallery => "UiPreset.Page.Gallery", UiPageKind.About => "UiPreset.Page.About", _ => page.ToString() };
        Presets = new(presets.GetPresets(page).Select(x => x.Metadata));
        if (!Presets.Any(x => string.Equals(x.Id, selection.PresetId, StringComparison.OrdinalIgnoreCase))) selection.PresetId = presets.GetDefault(page).Metadata.Id;
        _selection.EnsureActivePresetSettings();
        _selectedPresetId = selection.PresetId;
        RebuildSettings();
    }

    [ObservableProperty]
    private string _selectedPresetId = string.Empty;

    partial void OnSelectedPresetIdChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(_selection.PresetId, value, StringComparison.OrdinalIgnoreCase))
            return;

        _selection.SwitchPreset(value, _presets.GetRequired(value).CreateDefaultSettings());
        RebuildSettings();
    }

    private void RebuildSettings()
    {
        Settings.Clear();
        var preset = _presets.GetRequired(_selection.PresetId);
        foreach (var definition in preset.Settings)
        {
            if (!_selection.Settings.TryGetValue(definition.Key, out var value) || value is null)
            {
                value = definition.DefaultValue;
                // Only persist non-empty defaults into settings; empty Asset
                // values avoid overwriting top-level config properties.
                if (!string.IsNullOrWhiteSpace(value) || definition.Type != UiSettingType.Asset)
                    _selection.Settings[definition.Key] = value;
            }
            Settings.Add(new UiSettingEditorViewModel(definition, value, updated =>
            {
                _selection.Settings[definition.Key] = updated;
                _selection.SaveActivePresetSettings();
            }, _assets));
        }
    }

}

public sealed partial class UiSettingEditorViewModel : ObservableObject
{
    private readonly Action<string> _update;
    public UiSettingDefinition Definition { get; }
    public string DisplayNameKey => Definition.DisplayNameKey;
    public bool IsBoolean => Definition.Type == UiSettingType.Boolean;
    public bool IsSelect => Definition.Type == UiSettingType.Select;
    public bool IsInteger => Definition.Type == UiSettingType.Integer;
    public bool IsFloat => Definition.Type == UiSettingType.Float;
    public bool IsColor => Definition.Type == UiSettingType.Color;
    public bool IsAsset => Definition.Type == UiSettingType.Asset;
    public bool IsText => Definition.Type == UiSettingType.Text;
    public IReadOnlyList<UiSettingOption> Options => Definition.Options ?? [];
    public IAssetManager AssetManager { get; }
    public AssetPickerFilter AssetFilter => Definition.AssetFilter ?? AssetPickerFilter.All;

    public UiSettingEditorViewModel(UiSettingDefinition definition, string value, Action<string> update, IAssetManager assets)
    {
        Definition = definition;
        _value = value;
        _update = update;
        AssetManager = assets;
    }

    private string _value = string.Empty;
    public string Value
    {
        get => _value;
        set
        {
            // Avalonia creates every editor in the template, including hidden
            // controls. Some of those controls initialize their nullable value to
            // null and previously overwrote the real preset value during loading.
            if (value is null || !SetProperty(ref _value, value))
                return;

            _update(value);
            OnPropertyChanged(nameof(BooleanValue));
            OnPropertyChanged(nameof(IntegerValue));
            OnPropertyChanged(nameof(FloatValue));
            OnPropertyChanged(nameof(ColorValue));
        }
    }

    public bool BooleanValue { get => bool.TryParse(Value, out var value) ? value : bool.TryParse(Definition.DefaultValue, out var fallback) && fallback; set => Value = value.ToString().ToLowerInvariant(); }
    public decimal IntegerValue { get => decimal.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? decimal.Truncate(value) : ParseDefault(); set => Value = decimal.Truncate(value).ToString("0", CultureInfo.InvariantCulture); }
    public decimal FloatValue { get => decimal.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : ParseDefault(); set => Value = value.ToString(CultureInfo.InvariantCulture); }
    public decimal Minimum => Definition.Minimum is { } value ? (decimal)value : decimal.MinValue;
    public decimal Maximum => Definition.Maximum is { } value ? (decimal)value : decimal.MaxValue;
    public Color ColorValue { get => Color.TryParse(Value, out var value) ? value : Colors.Transparent; set => Value = value.ToString(); }
    private decimal ParseDefault() => decimal.TryParse(Definition.DefaultValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
}
