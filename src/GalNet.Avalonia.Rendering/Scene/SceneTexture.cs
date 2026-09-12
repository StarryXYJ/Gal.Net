using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using GalNet.Core.Assets;
using SkiaSharp;

namespace GalNet.Rendering.Scene;

/// <summary>
/// Immutable texture owned by the content host. It exposes an Avalonia image only for the
/// non-Skia fallback path and lazily decodes its Skia source once for render passes.
/// </summary>
public sealed class SceneTexture : IDisposable
{
    private readonly Lazy<SKBitmap?> _skBitmap;
    private readonly Lazy<SKImage?> _skImage;

    public SceneTexture(IImage image, SKBitmap? sourceBitmap = null)
    {
        AvaloniaImage = image ?? throw new ArgumentNullException(nameof(image));
        _skBitmap = new Lazy<SKBitmap?>(() => sourceBitmap ?? Decode(), LazyThreadSafetyMode.ExecutionAndPublication);
        _skImage = new Lazy<SKImage?>(() => SkBitmap is { } bitmap ? SKImage.FromBitmap(bitmap) : null, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>Loads Avalonia and Skia representations directly from the asset, never by re-encoding a bitmap as PNG.</summary>
    public static SceneTexture FromFile(string path)
    {
        var image = new Bitmap(path);
        try
        {
            var bitmap = SKBitmap.Decode(path);
            return new SceneTexture(image, bitmap);
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    /// <summary>Creates both render representations from encoded project-asset bytes.</summary>
    public static SceneTexture FromEncodedData(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        using var stream = new MemoryStream(data, writable: false);
        var image = new Bitmap(stream);
        try
        {
            var bitmap = SKBitmap.Decode(data);
            if (bitmap is null) throw new InvalidDataException("The image asset could not be decoded.");
            return new SceneTexture(image, bitmap);
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    public IImage AvaloniaImage { get; }
    public Size Size => AvaloniaImage.Size;
    public SKBitmap? SkBitmap => _skBitmap.Value;
    /// <summary>A stable image identity lets Skia cache the asset as a GPU texture across frames.</summary>
    public SKImage? SkImage => _skImage.Value;

    public void Dispose()
    {
        if (_skImage.IsValueCreated) _skImage.Value?.Dispose();
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

/// <summary>Renderer-owned decoder registered with the platform-neutral AssetManager.</summary>
public sealed class SceneTextureAssetDecoder : IAssetDecoder<SceneTexture>
{
    public ValueTask<SceneTexture?> DecodeAsync(IGameFile file, ReadOnlyMemory<byte> data, CancellationToken ct = default) =>
        ValueTask.FromResult<SceneTexture?>(SceneTexture.FromEncodedData(data.ToArray()));
}
