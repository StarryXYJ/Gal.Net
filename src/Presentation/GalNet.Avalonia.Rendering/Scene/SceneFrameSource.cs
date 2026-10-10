using Avalonia.Media;
using SkiaSharp;

namespace GalNet.Rendering.Scene;

/// <summary>A decoder-owned current texture, leased while the render thread reads it.</summary>
public sealed class SceneFrameSource : IDisposable
{
    private readonly object _gate = new();
    private SceneTexture? _texture;
    private bool _disposed;
    public event Action? FrameChanged;
    public event Action? Disposed;

    /// <summary>Transfers ownership of the bitmap, including when the source has already stopped.</summary>
    public void Publish(SKBitmap bitmap)
    {
        lock (_gate)
        {
            if (_disposed) { bitmap.Dispose(); return; }
            _texture?.Dispose();
            _texture = new SceneTexture(new DrawingImage(), bitmap);
        }
        FrameChanged?.Invoke();
    }

    public FrameLease AcquireFrame()
    {
        Monitor.Enter(_gate);
        return new FrameLease(_texture, _gate);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _texture?.Dispose();
            _texture = null;
        }
        Disposed?.Invoke();
    }

    public sealed class FrameLease(SceneTexture? texture, object gate) : IDisposable
    {
        public SceneTexture? Texture { get; } = texture;
        public void Dispose() => Monitor.Exit(gate);
    }
}
