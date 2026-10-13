using LibVLCSharp.Shared;
using GalNet.Runtime.Logging;

namespace GalNet.Sample.Avalonia.Services;

/// <summary>Owns the sample app's LibVLC instance across game presentation replacements.</summary>
internal sealed class SampleLibVlcRuntime : IDisposable
{
    public LibVLC? Instance { get; }

    public SampleLibVlcRuntime()
    {
        try
        {
            LibVLCSharp.Shared.Core.Initialize();
            Instance = new LibVLC("--no-video-title-show", "--avcodec-hw=none");
        }
        catch (Exception exception)
        {
            GameLog.Logger.Warning(exception,
                "Shared LibVLC initialization failed; presentation hosts will use their local fallback.");
        }
    }

    public void Dispose() => Instance?.Dispose();
}
