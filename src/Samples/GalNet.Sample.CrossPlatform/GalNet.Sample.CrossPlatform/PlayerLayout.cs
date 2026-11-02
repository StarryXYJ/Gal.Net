using Avalonia;

namespace GalNet.Sample.CrossPlatform;

/// <summary>Project-owned viewport breakpoints, shared by all platforms.</summary>
public static class PlayerLayout
{
    // Avalonia sizes are logical units; OS DPI scaling is already applied.
    public static bool IsPortrait(Size viewport) => viewport.Height > viewport.Width;

    public static double UiScale(Size viewport, bool mobile = false, double renderScaling = 1)
    {
        var scale = Math.Clamp(Math.Min(viewport.Width / 1600, viewport.Height / 900), mobile ? .7 : .65, 1.25);
        return mobile && renderScaling < 1.5 ? scale * 1.08 : scale;
    }
}
