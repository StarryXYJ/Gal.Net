using Avalonia;

namespace GalNet.Sample.CrossPlatform;

/// <summary>Project-owned viewport breakpoints, shared by all platforms.</summary>
public static class PlayerLayout
{
    // Avalonia sizes are logical units; OS DPI scaling is already applied.
    public static double UiScale(Size viewport) => Math.Clamp(Math.Min(viewport.Width / 1600, viewport.Height / 900), .75, 1);

    public static bool IsCompact(Size viewport) => viewport.Width < 1000 || viewport.Height < 600;
}
