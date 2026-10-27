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
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("The miniaudio adapter currently supports Windows x64.");
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

internal sealed unsafe class MiniaudioOutput : IAudioOutput
{
    private nint _device;
    private Native.Render? _callback;
    public void Start(AudioRenderCallback render)
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("The miniaudio device adapter currently supports Windows x64.");
        _callback = (samples, count) =>
        {
            var output = new Span<float>(samples, checked((int)count));
            try { render(output); } catch (Exception) { output.Clear(); } // Never unwind through a native callback.
        };
        _device = Native.gn_device_create(_callback);
        if (_device == 0) throw new InvalidOperationException("miniaudio could not open the default audio device.");
        if (Native.gn_device_start(_device) != 0) { Dispose(); throw new InvalidOperationException("miniaudio could not start the audio device."); }
    }
    public void Dispose()
    {
        if (_device != 0) { Native.gn_device_destroy(_device); _device = 0; }
        GC.KeepAlive(_callback); _callback = null;
    }
}

internal static unsafe class Native
{
    private const string Library = "galnet_miniaudio";
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void Render(float* output, uint samples);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint gn_device_create(Render render);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int gn_device_start(nint device);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void gn_device_destroy(nint device);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)] internal static extern nint gn_decoder_create(string path);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int gn_decoder_read(nint decoder, float* output, uint frames);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern double gn_decoder_duration(nint decoder);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void gn_decoder_destroy(nint decoder);
}
