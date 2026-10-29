using Avalonia.Media;

namespace GalNet.Avalonia.Themes;

/// <summary>Shared CJK-friendly UI typography for the player themes and their application host.</summary>
public static class GameTypography
{
    private static readonly string[] Families =
        ["Noto Sans CJK SC", "Noto Sans SC", "Source Han Sans SC", "Source Han Sans CN"];

    public static FontFamily UiFontFamily { get; } = FontFamily.Default;

    /// <summary>Return a fresh options object; do not share mutable font-manager options between hosts.</summary>
    public static FontManagerOptions CreateFontManagerOptions(string? defaultFamilyName = null) => new()
    {
        // Let the host choose its default, including an embedded font. CJK families
        // are character fallbacks, not prerequisites for creating the UI typeface.
        DefaultFamilyName = defaultFamilyName,
        FontFallbacks = Families.Select(name => new FontFallback { FontFamily = new FontFamily(name) }).ToArray()
    };
}
