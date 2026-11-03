using Avalonia.Controls;
using GalNet.Avalonia.GameHost.Settings;

namespace GalNet.Sample.CrossPlatform;

/// <summary>Design-space font sizes. Each role can be tuned independently by the project.</summary>
public sealed record PlayerFontSizes(double Title, double Heading, double Body, double Emphasis,
    double Action, double Auxiliary, double Dialogue);

public static class PlayerTypography
{
    public static FontSizePreset DesktopDefault { get; set; } = FontSizePreset.Smaller;
    public static FontSizePreset MobileDefault { get; set; } = FontSizePreset.Larger;
    public static FontSizePreset DefaultForPlatform(bool mobile) => mobile ? MobileDefault : DesktopDefault;
    public static FontSizePreset Default => DefaultForPlatform(OperatingSystem.IsAndroid() || OperatingSystem.IsIOS());

    public static IDictionary<FontSizePreset, PlayerFontSizes> Presets { get; } = new Dictionary<FontSizePreset, PlayerFontSizes>
    {
        [FontSizePreset.Small] = new(58, 30, 16, 24, 16, 11, 20),
        [FontSizePreset.Smaller] = new(64, 34, 18, 26, 18, 12, 22),
        [FontSizePreset.Larger] = new(72, 42, 24, 32, 24, 16, 30),
        [FontSizePreset.Large] = new(78, 46, 28, 36, 28, 20, 34)
    };

    public static void Apply(IResourceDictionary resources, FontSizePreset preset)
    {
        var sizes = Presets[preset];
        var values = new Dictionary<string, double>
        {
            ["TitleSize"] = sizes.Title,
            ["HeadingSize"] = sizes.Heading,
            ["BodyFontSize"] = sizes.Body,
            ["SceneBodyFontSize"] = sizes.Body,
            ["SpeakerFontSize"] = sizes.Emphasis,
            ["ActionFontSize"] = sizes.Action,
            ["AuxiliaryFontSize"] = sizes.Auxiliary,
            ["DialogueFontSize"] = sizes.Dialogue,
            // Preserve the existing desktop hierarchy while each alias follows its semantic role.
            ["MenuFontSize"] = sizes.Action * 20 / 18,
            ["GroupHeadingSize"] = sizes.Heading * 22 / 34,
            ["SettingsCategoryFontSize"] = sizes.Heading * 24 / 34,
            ["HistoryHeadingSize"] = sizes.Heading * 30 / 34,
            ["HistoryFontSize"] = sizes.Body * 21 / 18,
            ["HistorySpeakerFontSize"] = sizes.Emphasis * 18 / 26,
            ["SaveTitleFontSize"] = sizes.Body * 17 / 18,
            ["AboutTitleFontSize"] = sizes.Emphasis * 20 / 26,
            ["LoadingFontSize"] = sizes.Auxiliary * 16 / 12,
            ["ModalHeadingSize"] = sizes.Emphasis * 18 / 26,
            ["ModalFontSize"] = sizes.Body * 14 / 18,
            ["ModalLineHeight"] = sizes.Body * 22 / 18,
            ["LanguageHeadingSize"] = sizes.Heading * 28 / 34,
            ["SkipHeadingSize"] = sizes.Heading * 24 / 34,
            ["TrackTimeFontSize"] = sizes.Auxiliary * 11 / 12
        };
        if (values.Values.Any(value => !double.IsFinite(value) || value is < 1 or > 512))
            throw new ArgumentOutOfRangeException(nameof(preset), "Configured font sizes must be finite and between 1 and 512.");
        foreach (var (key, value) in values)
            resources["GalNet.Theme." + key] = value;
    }
}
