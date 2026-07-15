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
                ..TitleShared("center"), Integer("menuItemWidth", "UiPreset.Setting.MenuItemWidth", "260", 80, 800), Integer("menuItemHeight", "UiPreset.Setting.MenuItemHeight", "50", 24, 160),
                Color("menuItemBackgroundColor", "UiPreset.Setting.MenuItemBackgroundColor", "#FF8ED8FF"), Color("menuItemHoverBackgroundColor", "UiPreset.Setting.MenuItemHoverBackgroundColor", "#FFB5E7FF")]),
            new Definition("builtin.title.text-menu", UiPageKind.Title, "UiPreset.Title.TextMenu.Name", "UiPreset.Title.TextMenu.Description", [
                ..TitleShared("center"), Color("menuHoverTextColor", "UiPreset.Setting.MenuHoverTextColor", "#FF8ED8FF")]),
            new Definition("builtin.game.default", UiPageKind.Game, "UiPreset.DefaultGame.Name", "UiPreset.DefaultGame.Description", []),
            new Definition("builtin.settings.default", UiPageKind.Settings, "UiPreset.DefaultSettings.Name", "UiPreset.DefaultSettings.Description", []),
            new Definition("builtin.save-load.default", UiPageKind.SaveLoad, "UiPreset.DefaultSaveLoad.Name", "UiPreset.DefaultSaveLoad.Description", []),
            new Definition("builtin.gallery.default", UiPageKind.Gallery, "UiPreset.DefaultGallery.Name", "UiPreset.DefaultGallery.Description", [])
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
        Color("backgroundColor", "UiPreset.Setting.BackgroundColor", "#FF111118"),
        Asset("backgroundImage", "UiPreset.Setting.BackgroundImage", AssetPickerFilter.Image),
        Select("backgroundStretch", "UiPreset.Setting.BackgroundStretch", "uniformToFill", "uniformToFill", "uniform", "fill"),
        Integer("contentPadding", "UiPreset.Setting.ContentPadding", "0", 0, 500),
        Select("horizontalAlignment", "UiPreset.Setting.HorizontalAlignment", alignment, "left", "center", "right"),
        Integer("titleFontSize", "UiPreset.Setting.TitleFontSize", "48", 12, 160),
        Color("titleColor", "UiPreset.Setting.TitleColor", "#FFFFFFFF"),
        Integer("titleMenuGap", "UiPreset.Setting.TitleMenuGap", "14", 0, 300),
        Integer("menuFontSize", "UiPreset.Setting.MenuFontSize", "20", 8, 100),
        Color("menuTextColor", "UiPreset.Setting.MenuTextColor", "#FFFFFFFF"),
        Integer("menuSpacing", "UiPreset.Setting.MenuSpacing", "12", 0, 100),
        Bool("showGallery", "UiPreset.Setting.ShowGallery", "true")];
    private static UiSettingDefinition Integer(string key, string nameKey, string value, double min, double max) => new(key, nameKey, UiSettingType.Integer, value, Minimum: min, Maximum: max);
    private static UiSettingDefinition Color(string key, string nameKey, string value) => new(key, nameKey, UiSettingType.Color, value);
    private static UiSettingDefinition Asset(string key, string nameKey, AssetPickerFilter filter) => new(key, nameKey, UiSettingType.Asset, string.Empty, AssetFilter: filter);
    private static UiSettingDefinition Bool(string key, string nameKey, string value) => new(key, nameKey, UiSettingType.Boolean, value);
    private static UiSettingDefinition Select(string key, string nameKey, string value, params string[] options) => new(key, nameKey, UiSettingType.Select, value, options.Select(x => new UiSettingOption(x, $"UiPreset.Option.{x}" )).ToArray());
}
