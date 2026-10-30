using LibVLCSharp.Shared;

namespace GalNet.Media.LibVlc;

/// <summary>Owns the application's LibVLC instance across game presentation replacements.</summary>
public sealed class LibVlcRuntime : IDisposable
{
    public LibVLC? Instance { get; }
    public string? InitializationError { get; }
    public LibVlcFactory Factory { get; }

    /// <param name="create">Optional platform initialization and factory. The runtime owns the returned instance.</param>
    public LibVlcRuntime(Func<LibVLC>? create = null, LibVlcFactory? factory = null)
    {
        Factory = factory ?? LibVlcFactory.Default;
        try
        {
            if (create is null)
            {
                Instance = Factory.Create("--avcodec-hw=none");
            }
            else Instance = create() ?? throw new InvalidOperationException("The LibVLC factory returned null.");
        }
        catch (Exception exception)
        {
            InitializationError = exception.Message;
        }
    }

    public void Dispose() => Instance?.Dispose();
}
