using GalNet.Control.Abstraction.UI;
using GalNet.Core.UI;

namespace GalNet.Control.UI;

/// <summary>Built-in preset catalogue. Loading third-party catalogues later only extends this registry.</summary>
public sealed class BuiltInUiPresetRegistry : IUiPresetRegistry
{
    private readonly Dictionary<string, IUiPagePreset> _byId;

    public BuiltInUiPresetRegistry()
    {
        var presets = new IUiPagePreset[]
        {
            new Definition("builtin.title.button-menu", UiPageKind.Title, "UiPreset.Title.ButtonMenu.Name", "UiPreset.Title.ButtonMenu.Description", [
                ..TitleShared("center"),
                Integer("menuItemWidth", "UiPreset.Setting.MenuItemWidth", "260", 80, 800),
                Integer("menuItemHeight", "UiPreset.Setting.MenuItemHeight", "50", 24, 160),
                Color("menuItemBackgroundColor", "UiPreset.Setting.MenuItemBackgroundColor", "#FF8ED8FF"),
                Color("menuItemHoverBackgroundColor", "UiPreset.Setting.MenuItemHoverBackgroundColor", "#FFB5E7FF"),
                Color("menuHoverTextColor", "UiPreset.Setting.MenuHoverTextColor", "#FF8ED8FF")]),
            new Definition("builtin.title.text-menu", UiPageKind.Title, "UiPreset.Title.TextMenu.Name", "UiPreset.Title.TextMenu.Description", [
                ..TitleShared("center"),
                Color("menuHoverTextColor", "UiPreset.Setting.MenuHoverTextColor", "#FF8ED8FF"),
                Float("hoverScale", "UiPreset.Setting.HoverScale", "1.08", 0.5, 2)]),
            new Definition("builtin.game.default", UiPageKind.Game, "UiPreset.DefaultGame.Name", "UiPreset.DefaultGame.Description", GameSettings()),
            new Definition("builtin.settings.default", UiPageKind.Settings, "UiPreset.DefaultSettings.Name", "UiPreset.DefaultSettings.Description", SettingsScreenSettings()),
            new Definition("builtin.save-load.default", UiPageKind.SaveLoad, "UiPreset.DefaultSaveLoad.Name", "UiPreset.DefaultSaveLoad.Description", StandardScreenSettings()),
            new Definition("builtin.gallery.default", UiPageKind.Gallery, "UiPreset.DefaultGallery.Name", "UiPreset.DefaultGallery.Description", StandardScreenSettings()),
            new Definition("builtin.about.default", UiPageKind.About, "UiPreset.DefaultAbout.Name", "UiPreset.DefaultAbout.Description", AboutSettings())
        };
        _byId = presets.ToDictionary(x => x.Metadata.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<IUiPagePreset> GetPresets(UiPageKind page) => _byId.Values.Where(x => x.Metadata.Page == page).ToArray();
    public IUiPagePreset GetRequired(string id) => _byId.TryGetValue(id, out var preset) ? preset : throw new KeyNotFoundException($"UI preset '{id}' was not found.");
    public IUiPagePreset GetDefault(UiPageKind page) => GetPresets(page).First();

    private sealed class Definition(string id, UiPageKind page, string nameKey, string descriptionKey, IReadOnlyList<UiSettingDefinition> settings) : IUiPagePreset
    {
        public UiPresetMetadata Metadata { get; } = new(id, page, nameKey, descriptionKey);
        public IReadOnlyList<UiSettingDefinition> Settings { get; } = settings;
        public IReadOnlyDictionary<string, string> CreateDefaultSettings() => Settings.ToDictionary(x => x.Key, x => x.DefaultValue);
    }

    private static UiSettingDefinition[] TitleShared(string alignment) => [
        // Background and layout
        Color("backgroundColor", "UiPreset.Setting.BackgroundColor", "#FF111118"),
        Asset("backgroundImage", "UiPreset.Setting.BackgroundImage", AssetPickerFilter.Image),
        Select("backgroundStretch", "UiPreset.Setting.BackgroundStretch", "uniformToFill", "uniformToFill", "uniform", "fill"),
        Integer("contentPadding", "UiPreset.Setting.ContentPadding", "0", 0, 500),
        Select("horizontalAlignment", "UiPreset.Setting.HorizontalAlignment", alignment, "left", "center", "right"),
        // Title
        Color("titleColor", "UiPreset.Setting.TitleColor", "#FFFFFFFF"),
        Integer("titleFontSize", "UiPreset.Setting.TitleFontSize", "48", 12, 160),
        Integer("titleMenuGap", "UiPreset.Setting.TitleMenuGap", "14", 0, 300),
        // Menu text and visibility
        Integer("menuFontSize", "UiPreset.Setting.MenuFontSize", "20", 8, 100),
        Color("menuTextColor", "UiPreset.Setting.MenuTextColor", "#FFFFFFFF"),
        Integer("menuSpacing", "UiPreset.Setting.MenuSpacing", "12", 0, 100),
        Bool("showGallery", "UiPreset.Setting.ShowGallery", "true"),
        Bool("showAbout", "UiPreset.Setting.ShowAbout", "true")];

    private static UiSettingDefinition[] AboutSettings() => [
        // Content and layout
        Asset("contentAsset", "UiPreset.Setting.AboutContentAsset", AssetPickerFilter.Text),
        Integer("contentPadding", "UiPreset.Setting.ContentPadding", "20", 0, 500),
        Integer("fontSize", "UiPreset.Setting.FontSize", "16", 8, 100),
        // Surface and text
        Color("backgroundColor", "UiPreset.Setting.BackgroundColor", "#FF111118"),
        Color("panelColor", "UiPreset.Setting.PanelColor", "#FF292933"),
        Color("textColor", "UiPreset.Setting.TextColor", "#FFFFFFFF"),
        Color("headingColor", "UiPreset.Setting.HeadingColor", "#FFFFFFFF"),
        Color("selectionColor", "UiPreset.Setting.SelectionColor", "#668ED8FF"),
        // Links and code
        Color("linkColor", "UiPreset.Setting.LinkColor", "#FF8ED8FF"),
        Color("linkHoverColor", "UiPreset.Setting.LinkHoverColor", "#FFB5E7FF"),
        Color("linkVisitedColor", "UiPreset.Setting.LinkVisitedColor", "#FFC8C8D0"),
        Color("blockquoteBackgroundColor", "UiPreset.Setting.BlockquoteBackgroundColor", "#FF292933"),
        Color("blockquoteBorderColor", "UiPreset.Setting.BlockquoteBorderColor", "#FF8ED8FF"),
        Color("codeBackgroundColor", "UiPreset.Setting.CodeBackgroundColor", "#FF292933"),
        Color("codeBorderColor", "UiPreset.Setting.CodeBorderColor", "#FF8ED8FF"),
        Color("codeTextColor", "UiPreset.Setting.CodeTextColor", "#FFFFFFFF"),
        Integer("codeFontSize", "UiPreset.Setting.CodeFontSize", "16", 8, 100),
        Color("ruleColor", "UiPreset.Setting.RuleColor", "#FF989AAF"),
        Color("backButtonForegroundColor", "UiPreset.Setting.BackButtonForegroundColor", "#FFFFFFFF")];

    private static UiSettingDefinition[] GameSettings() => [
        // Dialogue surface and typography
        Color("dialogueBackgroundColor", "UiPreset.Setting.DialogueBackgroundColor", "#CC292933"),
        Asset("dialogueBackgroundImage", "UiPreset.Setting.DialogueBackgroundImage", AssetPickerFilter.Image),
        Float("dialogueBackgroundImageOpacity", "UiPreset.Setting.ImageOpacity", "1", 0, 1),
        Color("dialogueTextColor", "UiPreset.Setting.DialogueTextColor", "#FFFFFFFF"),
        Color("speakerTextColor", "UiPreset.Setting.SpeakerTextColor", "#FF8ED8FF"),
        Integer("dialogueHeight", "UiPreset.Setting.DialogueHeight", "160", 80, 600),
        Integer("dialogueMargin", "UiPreset.Setting.DialogueMargin", "20", 0, 200),
        Integer("dialogueCornerRadius", "UiPreset.Setting.DialogueCornerRadius", "8", 0, 100),
        Integer("dialogueFontSize", "UiPreset.Setting.DialogueFontSize", "16", 8, 100),
        // Choice layout and buttons
        Select("choiceLayout", "UiPreset.Setting.ChoiceLayout", "vertical", "vertical", "horizontal"),
        Color("choiceButtonColor", "UiPreset.Setting.ChoiceButtonColor", "#FF292933"),
        Color("choiceButtonTextColor", "UiPreset.Setting.ChoiceButtonTextColor", "#FFFFFFFF"),
        Integer("choiceButtonWidth", "UiPreset.Setting.ChoiceButtonWidth", "240", 80, 800),
        Integer("choiceButtonHeight", "UiPreset.Setting.ChoiceButtonHeight", "44", 24, 200),
        Integer("choiceSpacing", "UiPreset.Setting.ChoiceSpacing", "8", 0, 100),
        // Command bar
        Bool("commandBarVisible", "UiPreset.Setting.CommandBarVisible", "true"),
        Color("commandTextColor", "UiPreset.Setting.CommandTextColor", "#FFC8C8D0"),
        Color("commandHoverTextColor", "UiPreset.Setting.CommandHoverTextColor", "#FF8ED8FF"),
        Color("commandSelectedTextColor", "UiPreset.Setting.CommandSelectedTextColor", "#FF8ED8FF")];

    private static UiSettingDefinition[] StandardScreenSettings() => [
        // Page and panel surface
        Color("backgroundColor", "UiPreset.Setting.BackgroundColor", "#FF111118"),
        Color("panelColor", "UiPreset.Setting.PanelColor", "#FF292933"),
        // Button appearance
        Color("buttonColor", "UiPreset.Setting.ButtonColor", "#FF292933"),
        Color("buttonTextColor", "UiPreset.Setting.ButtonTextColor", "#FFFFFFFF"),
        Color("backButtonForegroundColor", "UiPreset.Setting.BackButtonForegroundColor", "#FFFFFFFF"),
        // General text
        Color("textColor", "UiPreset.Setting.TextColor", "#FFFFFFFF"),
        ];

    private static UiSettingDefinition[] SettingsScreenSettings() => [
        ..StandardScreenSettings(),
        // Slider and checkbox controls
        Color("sliderTrackColor", "UiPreset.Setting.SliderTrackColor", "#665F6075"),
        Color("sliderFillColor", "UiPreset.Setting.SliderFillColor", "#FF8ED8FF"),
        Color("sliderThumbColor", "UiPreset.Setting.SliderThumbColor", "#FFFFFFFF"),
        Color("sliderThumbBorderColor", "UiPreset.Setting.SliderThumbBorderColor", "#665F6075"),
        Color("checkBoxBorderColor", "UiPreset.Setting.CheckBoxBorderColor", "#FF989AAF"),
        Color("checkBoxFillColor", "UiPreset.Setting.CheckBoxFillColor", "#FF8ED8FF"),
        Color("checkBoxCheckColor", "UiPreset.Setting.CheckBoxCheckColor", "#FF111118")];
    private static UiSettingDefinition Integer(string key, string nameKey, string value, double min, double max) => new(key, nameKey, UiSettingType.Integer, value, Minimum: min, Maximum: max);
    private static UiSettingDefinition Float(string key, string nameKey, string value, double min, double max) => new(key, nameKey, UiSettingType.Float, value, Minimum: min, Maximum: max);
    private static UiSettingDefinition Color(string key, string nameKey, string value) => new(key, nameKey, UiSettingType.Color, value);
    private static UiSettingDefinition Asset(string key, string nameKey, AssetPickerFilter filter) => new(key, nameKey, UiSettingType.Asset, string.Empty, AssetFilter: filter);
    private static UiSettingDefinition Bool(string key, string nameKey, string value) => new(key, nameKey, UiSettingType.Boolean, value);
    private static UiSettingDefinition Select(string key, string nameKey, string value, params string[] options) => new(key, nameKey, UiSettingType.Select, value, options.Select(x => new UiSettingOption(x, $"UiPreset.Option.{x}" )).ToArray());
}
