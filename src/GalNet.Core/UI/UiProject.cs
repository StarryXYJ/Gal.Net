namespace GalNet.Core.UI;

/// <summary>Identifiers for the fixed game pages. Presets may change their presentation, not this flow.</summary>
public enum UiPageKind { Title, Game, Settings, SaveLoad, Gallery, About }

/// <summary>The selected presentation and its preset-owned settings for one fixed game page.</summary>
public sealed class UiPageSelection
{
    public string PresetId { get; set; } = string.Empty;
    public Dictionary<string, string> Settings { get; set; } = [];
    /// <summary>Settings retained for every preset previously selected on this page.</summary>
    public Dictionary<string, Dictionary<string, string>> PresetSettings { get; set; } = [];

    public void EnsureActivePresetSettings()
    {
        if (!PresetSettings.ContainsKey(PresetId))
            PresetSettings[PresetId] = new(Settings);
    }

    public void SaveActivePresetSettings() => PresetSettings[PresetId] = new(Settings);

    public void SwitchPreset(string presetId, IReadOnlyDictionary<string, string> defaults)
    {
        SaveActivePresetSettings();
        PresetId = presetId;
        Settings = PresetSettings.TryGetValue(presetId, out var saved) ? new(saved) : new(defaults);
        EnsureActivePresetSettings();
    }
}

/// <summary>Serializable UI document. Each fixed page chooses its own presentation preset.</summary>
public sealed class UiProject
{
    public int Version { get; set; } = 3;
    public string ColorPaletteId { get; set; } = UiColorPalettePresets.DefaultId;
    public Dictionary<UiPageKind, UiPageSelection> Pages { get; set; } = CreateDefaultPages();

    public UiPageSelection GetPage(UiPageKind page)
    {
        if (Pages.TryGetValue(page, out var selection)) return selection;
        selection = CreateDefaultPages()[page];
        Pages[page] = selection;
        return selection;
    }

    private static Dictionary<UiPageKind, UiPageSelection> CreateDefaultPages() => new()
    {
        [UiPageKind.Title] = new() { PresetId = "builtin.title.button-menu" },
        [UiPageKind.Game] = new() { PresetId = "builtin.game.default" },
        [UiPageKind.Settings] = new() { PresetId = "builtin.settings.default" },
        [UiPageKind.SaveLoad] = new() { PresetId = "builtin.save-load.default" },
        [UiPageKind.Gallery] = new() { PresetId = "builtin.gallery.default" },
        [UiPageKind.About] = new() { PresetId = "builtin.about.default" }
    };
}
