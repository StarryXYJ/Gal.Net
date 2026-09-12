using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace GalNet.Rendering.Scene;

/// <summary>
/// Immutable texture owned by the content host. It exposes an Avalonia image only for the
/// non-Skia fallback path and lazily decodes its Skia source once for render passes.
/// </summary>
public sealed class SceneTexture : IDisposable
{
    private readonly Lazy<SKBitmap?> _skBitmap;

    public SceneTexture(IImage image)
    {
        AvaloniaImage = image ?? throw new ArgumentNullException(nameof(image));
        _skBitmap = new Lazy<SKBitmap?>(Decode, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public IImage AvaloniaImage { get; }
    public Size Size => AvaloniaImage.Size;
    public SKBitmap? SkBitmap => _skBitmap.Value;

    public void Dispose()
    {
        if (_skBitmap.IsValueCreated) _skBitmap.Value?.Dispose();
    }

    private SKBitmap? Decode()
    {
        if (AvaloniaImage is not Bitmap bitmap) return null;
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        stream.Position = 0;
        return SKBitmap.Decode(stream);
    }
}

/// <summary>Content boundary used by Layers and flipbooks to obtain their current texture.</summary>
public interface ISceneTextureResolver
{
    SceneTexture ResolveTexture(string assetId);
}
