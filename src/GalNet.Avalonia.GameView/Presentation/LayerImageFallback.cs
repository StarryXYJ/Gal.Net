using System.Collections.Concurrent;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace GalNet.Avalonia.GameView.Presentation;

/// <summary>Process-wide fallback image and diagnostic de-duplication for unresolved Layer assets.</summary>
public static class LayerImageFallback
{
    private const string ResourceName = "GalNet.Avalonia.GameView.Assets.MissingLayer.png";
    private static readonly Lazy<IImage> Image = new(LoadImage, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly ConcurrentDictionary<string, byte> ReportedAssets = new(StringComparer.Ordinal);

    /// <summary>Always-available built-in image used when a content Layer asset cannot be loaded.</summary>
    public static IImage MissingImage => Image.Value;

    /// <summary>Returns true once per missing asset so callers can log without frame-by-frame noise.</summary>
    public static bool ShouldReport(string assetId) => ReportedAssets.TryAdd(assetId, 0);

    private static Bitmap LoadImage()
    {
        using var stream = typeof(LayerImageFallback).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded fallback image '{ResourceName}' was not found.");
        return new Bitmap(stream);
    }
}
