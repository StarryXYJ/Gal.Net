using Avalonia.Media;

namespace GalNet.Core.UI;

/// <summary>Built-in project-wide UI colour palettes. Presets only replace colours; layout and assets are retained.</summary>
public sealed record UiColorPalettePreset(
    string Id,
    string DisplayNameKey,
    Color PreviewColor,
    Color BackgroundColor,
    Color PanelColor,
    Color TextColor,
    Color AccentColor,
    Color AccentHoverColor,
    Color MutedTextColor,
    Color BorderColor,
    Color CheckMarkColor);

public static class UiColorPalettePresets
{
    public const string DefaultId = "midnight-blue";

    public static IReadOnlyList<UiColorPalettePreset> All { get; } =
    [
        new(DefaultId, "UiColorPalette.MidnightBlue", Color.Parse("#FF8ED8FF"),
            Color.Parse("#FF111118"), Color.Parse("#FF292933"), Colors.White,
            Color.Parse("#FF8ED8FF"), Color.Parse("#FFB5E7FF"), Color.Parse("#FFC8C8D0"),
            Color.Parse("#FF989AAF"), Color.Parse("#FF111118")),
        new("rose-dusk", "UiColorPalette.RoseDusk", Color.Parse("#FFFF8EBC"),
            Color.Parse("#FF1A1018"), Color.Parse("#FF382333"), Color.Parse("#FFFFF4F8"),
            Color.Parse("#FFFF8EBC"), Color.Parse("#FFFFB5D1"), Color.Parse("#FFE8C9D5"),
            Color.Parse("#FFC99AAA"), Color.Parse("#FF30111F"))
    ];

    public static UiColorPalettePreset GetRequired(string? id) =>
        All.FirstOrDefault(palette => string.Equals(palette.Id, id, StringComparison.OrdinalIgnoreCase)) ?? All[0];

    public static void Apply(UiProject project, string? id)
    {
        var palette = GetRequired(id);
        project.ColorPaletteId = palette.Id;

        project.Title.BackgroundColor = palette.BackgroundColor;
        project.Title.TitleColor = palette.TextColor;
        project.Title.MenuTextColor = palette.TextColor;
        project.Title.MenuHoverTextColor = palette.AccentColor;
        project.Title.ButtonColor = palette.AccentColor;
        project.Title.ButtonTextColor = palette.CheckMarkColor;
        project.Title.ButtonHoverColor = palette.AccentHoverColor;

        project.Game.DialogueBackgroundColor = WithOpacity(palette.PanelColor, 0.8);
        project.Game.DialogueTextColor = palette.TextColor;
        project.Game.SpeakerTextColor = palette.AccentColor;
        project.Game.ChoiceButtonColor = palette.PanelColor;
        project.Game.ChoiceButtonTextColor = palette.TextColor;
        project.Game.CommandTextColor = palette.MutedTextColor;
        project.Game.CommandHoverTextColor = palette.AccentColor;
        project.Game.CommandSelectedTextColor = palette.AccentColor;

        ApplyStandard(project.Settings, palette);
        ApplyStandard(project.SaveLoad, palette);
        ApplyStandard(project.Gallery, palette);

        ApplyTitleSettings(project.GetPage(UiPageKind.Title), palette);
        ApplyGameSettings(project.GetPage(UiPageKind.Game), palette);
        ApplyStandardSettings(project.GetPage(UiPageKind.Settings), palette, includeSettingsControls: true);
        ApplyStandardSettings(project.GetPage(UiPageKind.SaveLoad), palette, includeSettingsControls: false);
        ApplyStandardSettings(project.GetPage(UiPageKind.Gallery), palette, includeSettingsControls: false);
    }

    private static void ApplyStandard(SettingsUiConfiguration config, UiColorPalettePreset palette)
    {
        config.BackgroundColor = palette.BackgroundColor;
        config.PanelColor = palette.PanelColor;
        config.TextColor = palette.TextColor;
        config.ButtonColor = palette.PanelColor;
        config.ButtonTextColor = palette.TextColor;
        config.BackButtonForegroundColor = palette.TextColor;
        config.SliderTrackColor = WithOpacity(palette.BorderColor, 0.4);
        config.SliderFillColor = palette.AccentColor;
        config.SliderThumbColor = palette.TextColor;
        config.SliderThumbBorderColor = palette.BorderColor;
        config.CheckBoxBorderColor = palette.BorderColor;
        config.CheckBoxFillColor = palette.AccentColor;
        config.CheckBoxCheckColor = palette.CheckMarkColor;
    }

    private static void ApplyTitleSettings(UiPageSelection selection, UiColorPalettePreset palette) =>
        UpdateAllSettings(selection, values =>
        {
            values["backgroundColor"] = Format(palette.BackgroundColor);
            values["titleColor"] = Format(palette.TextColor);
            values["menuTextColor"] = Format(palette.TextColor);
            values["menuHoverTextColor"] = Format(palette.AccentColor);
            values["menuItemBackgroundColor"] = Format(palette.AccentColor);
            values["menuItemHoverBackgroundColor"] = Format(palette.AccentHoverColor);
        });

    private static void ApplyGameSettings(UiPageSelection selection, UiColorPalettePreset palette) =>
        UpdateAllSettings(selection, values =>
        {
            values["dialogueBackgroundColor"] = Format(WithOpacity(palette.PanelColor, 0.8));
            values["dialogueTextColor"] = Format(palette.TextColor);
            values["speakerTextColor"] = Format(palette.AccentColor);
            values["choiceButtonColor"] = Format(palette.PanelColor);
            values["choiceButtonTextColor"] = Format(palette.TextColor);
            values["commandTextColor"] = Format(palette.MutedTextColor);
            values["commandHoverTextColor"] = Format(palette.AccentColor);
            values["commandSelectedTextColor"] = Format(palette.AccentColor);
        });

    private static void ApplyStandardSettings(UiPageSelection selection, UiColorPalettePreset palette, bool includeSettingsControls) =>
        UpdateAllSettings(selection, values =>
        {
            values["backgroundColor"] = Format(palette.BackgroundColor);
            values["panelColor"] = Format(palette.PanelColor);
            values["textColor"] = Format(palette.TextColor);
            values["buttonColor"] = Format(palette.PanelColor);
            values["buttonTextColor"] = Format(palette.TextColor);
            values["backButtonForegroundColor"] = Format(palette.TextColor);
            if (!includeSettingsControls) return;
            values["sliderTrackColor"] = Format(WithOpacity(palette.BorderColor, 0.4));
            values["sliderFillColor"] = Format(palette.AccentColor);
            values["sliderThumbColor"] = Format(palette.TextColor);
            values["sliderThumbBorderColor"] = Format(palette.BorderColor);
            values["checkBoxBorderColor"] = Format(palette.BorderColor);
            values["checkBoxFillColor"] = Format(palette.AccentColor);
            values["checkBoxCheckColor"] = Format(palette.CheckMarkColor);
        });

    private static void UpdateAllSettings(UiPageSelection selection, Action<Dictionary<string, string>> update)
    {
        update(selection.Settings);
        foreach (var settings in selection.PresetSettings.Values)
            update(settings);
    }

    private static Color WithOpacity(Color color, double opacity) => Color.FromArgb((byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255), color.R, color.G, color.B);
    private static string Format(Color color) => color.ToString();
}
