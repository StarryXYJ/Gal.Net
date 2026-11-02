using Avalonia;

namespace GalNet.Sample.CrossPlatform;

/// <summary>Project-owned viewport breakpoints, shared by all platforms.</summary>
public static class PlayerLayout
{
    public static bool IsCompact(Size viewport) => viewport.Width < 1000 || viewport.Height < 600;
}
