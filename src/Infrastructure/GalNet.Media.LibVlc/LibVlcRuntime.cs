using LibVLCSharp.Shared;

namespace GalNet.Media.LibVlc;

/// <summary>Owns the application's LibVLC instance across game presentation replacements.</summary>
public sealed class LibVlcRuntime : IDisposable
{
    public LibVLC? Instance { get; }
    public string? InitializationError { get; }

    public LibVlcRuntime()
    {
        try
        {
            LibVLCSharp.Shared.Core.Initialize();
            Instance = new LibVLC("--no-video-title-show", "--avcodec-hw=none");
        }
        catch (Exception exception)
        {
            InitializationError = exception.Message;
        }
    }

    public void Dispose() => Instance?.Dispose();
}
