using Avalonia.Media;

namespace GalNet.Avalonia.Themes;

/// <summary>Shared CJK-friendly UI typography for the player themes and their application host.</summary>
public static class GameTypography
{
    private static readonly string[] Families = OperatingSystem.IsMacOS()
        ? ["PingFang SC", "Noto Sans CJK SC", "Noto Sans SC", "Microsoft YaHei UI", "Microsoft YaHei"]
        : OperatingSystem.IsWindows()
            ? ["Microsoft YaHei UI", "Microsoft YaHei", "Noto Sans CJK SC", "Noto Sans SC", "PingFang SC"]
            : ["Noto Sans CJK SC", "Noto Sans SC", "WenQuanYi Micro Hei", "Microsoft YaHei UI", "PingFang SC"];

    public static FontFamily UiFontFamily { get; } = new(string.Join(", ", Families));

    /// <summary>Return a fresh options object; do not share mutable font-manager options between hosts.</summary>
    public static FontManagerOptions CreateFontManagerOptions() => new()
    {
        // Linux distributions choose their own installed default; CJK preferences still apply below.
        DefaultFamilyName = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? Families[0] : null,
        FontFallbacks = Families.Select(name => new FontFallback { FontFamily = new FontFamily(name) }).ToArray()
    };
}
