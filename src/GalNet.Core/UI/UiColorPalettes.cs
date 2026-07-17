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
            Color.Parse("#FFC99AAA"), Color.Parse("#FF30111F")),
        new("emerald-night", "UiColorPalette.EmeraldNight", Color.Parse("#FF55D6A9"),
            Color.Parse("#FF0E1714"), Color.Parse("#FF1C2A25"), Color.Parse("#FFEAF7F1"),
            Color.Parse("#FF55D6A9"), Color.Parse("#FF82E5C0"), Color.Parse("#FFB7CEC5"),
            Color.Parse("#FF6F9184"), Color.Parse("#FF08271D")),
        new("amber-noir", "UiColorPalette.AmberNoir", Color.Parse("#FFF4B860"),
            Color.Parse("#FF17130D"), Color.Parse("#FF2B2418"), Color.Parse("#FFFFF8E8"),
            Color.Parse("#FFF4B860"), Color.Parse("#FFFFD08A"), Color.Parse("#FFD5C5A7"),
            Color.Parse("#FF9A8260"), Color.Parse("#FF2C1A00")),
        new("daylight-sky", "UiColorPalette.DaylightSky", Color.Parse("#FF2878D0"),
            Color.Parse("#FFF4F8FC"), Color.Parse("#FFFFFFFF"), Color.Parse("#FF1A2633"),
            Color.Parse("#FF2878D0"), Color.Parse("#FF4A93E2"), Color.Parse("#FF5F7185"),
            Color.Parse("#FFAAB8C6"), Color.Parse("#FFFFFFFF")),
        new("violet-neon", "UiColorPalette.VioletNeon", Color.Parse("#FFB58CFF"),
            Color.Parse("#FF120F1B"), Color.Parse("#FF292139"), Color.Parse("#FFF7F1FF"),
            Color.Parse("#FFB58CFF"), Color.Parse("#FFCFB3FF"), Color.Parse("#FFC8B9DD"),
            Color.Parse("#FF83749B"), Color.Parse("#FF1A102B"))
    ];

    public static UiColorPalettePreset GetRequired(string? id) =>
        All.FirstOrDefault(palette => string.Equals(palette.Id, id, StringComparison.OrdinalIgnoreCase)) ?? All[0];

    public static void Apply(UiProject project, string? id)
    {
        var palette = GetRequired(id);
        project.ColorPaletteId = palette.Id;

        ApplyTitleSettings(project.GetPage(UiPageKind.Title), palette);
        ApplyGameSettings(project.GetPage(UiPageKind.Game), palette);
        ApplyStandardSettings(project.GetPage(UiPageKind.Settings), palette, includeSettingsControls: true);
        ApplyStandardSettings(project.GetPage(UiPageKind.SaveLoad), palette, includeSettingsControls: false);
        ApplyStandardSettings(project.GetPage(UiPageKind.Gallery), palette, includeSettingsControls: false);
        ApplyAboutSettings(project.GetPage(UiPageKind.About), palette);
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

    private static void ApplyAboutSettings(UiPageSelection selection, UiColorPalettePreset palette) =>
        UpdateAllSettings(selection, values =>
        {
            values["backgroundColor"] = Format(palette.BackgroundColor);
            values["panelColor"] = Format(palette.PanelColor);
            values["textColor"] = Format(palette.TextColor);
            values["headingColor"] = Format(palette.TextColor);
            values["selectionColor"] = Format(WithOpacity(palette.AccentColor, 0.4));
            values["linkColor"] = Format(palette.AccentColor);
            values["linkHoverColor"] = Format(palette.AccentHoverColor);
            values["linkVisitedColor"] = Format(palette.MutedTextColor);
            values["blockquoteBackgroundColor"] = Format(palette.PanelColor);
            values["blockquoteBorderColor"] = Format(palette.AccentColor);
            values["codeBackgroundColor"] = Format(palette.PanelColor);
            values["codeBorderColor"] = Format(palette.AccentColor);
            values["codeTextColor"] = Format(palette.TextColor);
            values["ruleColor"] = Format(palette.BorderColor);
            values["backButtonForegroundColor"] = Format(palette.TextColor);
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
