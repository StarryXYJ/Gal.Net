using GalNet.Core.Audio;

namespace GalNet.Presentation.Abstractions.Audio;

/// <summary>Platform playback boundary. Hosts execute voice commands but never own queues.</summary>
public interface IAudioPlaybackHost : IDisposable
{
    AudioBackendCapabilities Capabilities { get; }
    event Action<AudioBackendEvent>? EventReceived;
    Task ApplyAsync(AudioBackendRequest request, CancellationToken cancellationToken = default);
}
