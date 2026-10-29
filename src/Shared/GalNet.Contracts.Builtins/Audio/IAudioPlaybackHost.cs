namespace GalNet.Contracts.Builtins.Audio;

/// <summary>Playback and target-mixer boundary. Hosts execute voice/graph commands but never own queues.</summary>
public interface IAudioPlaybackHost : IDisposable
{
    AudioBackendCapabilities Capabilities { get; }
    ISEffectCatalog SEffects { get; }
    event Action<AudioBackendEvent>? EventReceived;
    Task ApplyAsync(AudioBackendRequest request, CancellationToken cancellationToken = default);
}
