using System.Runtime.InteropServices;
using GalNet.Rendering.Scene;
using LibVLCSharp.Shared;
using SkiaSharp;

namespace GalNet.Avalonia.GameView.Services;

/// <summary>CPU video output shared by scene video, Gallery and thumbnails. Dispose after stopping its player.</summary>
internal sealed class LibVlcFrameOutput : IDisposable
{
    private readonly Action<Exception> _failed;
    private IntPtr _allocation;
    private IntPtr _pixels;
    private int _width;
    private int _height;
    private int _pitch;
    public SceneFrameSource Frames { get; }

    public LibVlcFrameOutput(MediaPlayer player, Action<Exception> failed, SceneFrameSource? frames = null)
    {
        Frames = frames ?? new();
        _failed = failed;
        player.SetVideoFormatCallbacks(Format, Cleanup);
        player.SetVideoCallbacks(Lock, null, Display);
    }

    private uint Format(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height, ref uint pitches, ref uint lines)
    {
        try
        {
            FreeBuffer();
            if (width == 0 || height == 0 || width > 8192 || height > 8192) throw new InvalidDataException("Unsupported video dimensions.");
            _width = checked((int)width);
            _height = checked((int)height);
            _pitch = checked((_width * 4 + 31) & ~31);
            pitches = (uint)_pitch;
            lines = (height + 31) & ~31u;
            Marshal.Copy(new byte[] { (byte)'R', (byte)'V', (byte)'3', (byte)'2' }, 0, chroma, 4);
            _allocation = Marshal.AllocHGlobal(checked(_pitch * (int)lines + 31));
            _pixels = new IntPtr((_allocation.ToInt64() + 31) & ~31L);
            return 1;
        }
        catch (Exception error) { _failed(error); return 0; }
    }

    private IntPtr Lock(IntPtr opaque, IntPtr planes)
    {
        Marshal.WriteIntPtr(planes, _pixels);
        return IntPtr.Zero;
    }

    private void Display(IntPtr opaque, IntPtr picture)
    {
        if (_pixels == IntPtr.Zero) return;
        try
        {
            using var decoded = new SKBitmap();
            decoded.InstallPixels(new SKImageInfo(_width, _height, SKColorType.Bgra8888, SKAlphaType.Opaque), _pixels, _pitch);
            Frames.Publish(decoded.Copy());
        }
        catch (Exception error) { _failed(error); }
    }

    private void Cleanup(ref IntPtr opaque) => FreeBuffer();
    private void FreeBuffer()
    {
        _pixels = IntPtr.Zero;
        if (_allocation != IntPtr.Zero) Marshal.FreeHGlobal(_allocation);
        _allocation = IntPtr.Zero;
    }

    public void Dispose()
    {
        Frames.Dispose();
        FreeBuffer();
    }
}
