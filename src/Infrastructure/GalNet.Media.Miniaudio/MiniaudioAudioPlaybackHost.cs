using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GalNet.Core.Assets;
using GalNet.Media.Audio;

namespace GalNet.Media.Miniaudio;

public sealed class MiniaudioAudioPlaybackHost(IAssetFileMaterializer files)
    : PcmAudioPlaybackHost(files, path => new MiniaudioDecoder(path), new MiniaudioOutput());

public sealed unsafe class MiniaudioDecoder : IAudioPcmDecoder
{
    private nint _decoder;
    public double DurationSeconds { get; }
    public MiniaudioDecoder(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path.Contains('\0')) throw new ArgumentException("Audio paths cannot contain a null character.", nameof(path));
        Native.EnsureCompatible();
        _decoder = Native.gn_decoder_create(path);
        if (_decoder == 0) throw new InvalidDataException($"miniaudio could not decode '{Path.GetFileName(path)}' (supported: WAV, MP3, Ogg Vorbis, FLAC).");
        DurationSeconds = Native.gn_decoder_duration(_decoder);
    }
    public int Read(Span<float> samples)
    {
        ObjectDisposedException.ThrowIf(_decoder == 0, this);
        fixed (float* output = samples)
        {
            var frames = Native.gn_decoder_read(_decoder, output, (uint)(samples.Length / 2));
            if (frames < 0) throw new InvalidDataException("miniaudio PCM decoding failed.");
            return frames * 2;
        }
    }
    public void Dispose() { if (_decoder != 0) { Native.gn_decoder_destroy(_decoder); _decoder = 0; } }
}

public sealed unsafe class MiniaudioOutput : IAudioOutput, IAudioOutputLifecycle
{
    private readonly object _gate = new();
    private nint _device;
    private GCHandle _context;
    private bool _disposed;
    public void Start(AudioRenderCallback render)
    {
        ArgumentNullException.ThrowIfNull(render);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_context.IsAllocated) throw new InvalidOperationException("The audio output has already been started.");
            Native.EnsureCompatible();
            _context = GCHandle.Alloc(render);
            try
            {
                OpenDevice();
            }
            catch { Release(); throw; }
        }
    }

    public void SuspendOutput()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_context.IsAllocated) throw new InvalidOperationException("The audio output has not been started.");
            // Release the endpoint while retaining the callback and the host's PCM/DSP state.
            CloseDevice();
        }
    }

    public void ResumeOutput()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_context.IsAllocated) throw new InvalidOperationException("The audio output has not been started.");
            if (_device == 0) OpenDevice(); // Resolve the current default endpoint after an interruption.
        }
    }

    private void OpenDevice()
    {
        _device = Native.gn_device_create(&Render, GCHandle.ToIntPtr(_context));
        if (_device == 0) throw new InvalidOperationException("miniaudio could not open the default audio device.");
        if (Native.gn_device_start(_device) != 0)
        {
            CloseDevice();
            throw new InvalidOperationException("miniaudio could not start the audio device.");
        }
    }

    private void CloseDevice()
    {
        if (_device != 0) { Native.gn_device_destroy(_device); _device = 0; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Render(nint context, float* samples, uint count)
    {
        var output = new Span<float>(samples, (int)count);
        try { ((AudioRenderCallback)GCHandle.FromIntPtr(context).Target!)(output); }
        catch (Exception) { output.Clear(); } // Never unwind through a native callback.
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            Release();
        }
    }
    private void Release()
    {
        // Uninitializing the device waits for callbacks before the context can be freed.
        CloseDevice();
        if (_context.IsAllocated) _context.Free();
    }
}

internal static unsafe partial class Native
{
#if GALNET_MINIAUDIO_STATIC
    private const string Library = "__Internal";
#else
    private const string Library = "galnet_miniaudio";
#endif
    internal static void EnsureCompatible()
    {
        if (gn_abi_version() != 2) throw new InvalidOperationException("The miniaudio native library must use GalNet ABI 2. Rebuild and deploy it with the managed adapter.");
    }

    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int gn_abi_version();
    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint gn_device_create(delegate* unmanaged[Cdecl]<nint, float*, uint, void> render, nint context);
    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int gn_device_start(nint device);
    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void gn_device_destroy(nint device);
    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint gn_decoder_create(string path);
    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int gn_decoder_read(nint decoder, float* output, uint frames);
    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial double gn_decoder_duration(nint decoder);
    [LibraryImport(Library)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void gn_decoder_destroy(nint decoder);
}
