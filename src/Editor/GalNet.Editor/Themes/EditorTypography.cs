using Avalonia.Controls;

namespace GalNet.Editor.Themes;

/// <summary>Editor typography roles, scaled together from the user-selected body size.</summary>
public static class EditorTypography
{
    public const double DefaultSize = 12;
    public const double MinimumSize = 10;
    public const double MaximumSize = 18;

    public static double Normalize(double size) => double.IsFinite(size) ? Math.Clamp(size, MinimumSize, MaximumSize) : DefaultSize;

    public static void Apply(IResourceDictionary resources, double size)
    {
        var scale = Normalize(size) / DefaultSize;
        resources["Editor.Font.Micro"] = 10d * scale;
        resources["Editor.Font.Caption"] = 10.5d * scale;
        resources["Editor.Font.Detail"] = 11d * scale;
        resources["Editor.Font.Body"] = 12d * scale;
        resources["Editor.Font.Emphasis"] = 13d * scale;
        resources["Editor.Font.Heading"] = 14d * scale;
        resources["Editor.Font.Hero"] = 30d * scale;
    }
}
