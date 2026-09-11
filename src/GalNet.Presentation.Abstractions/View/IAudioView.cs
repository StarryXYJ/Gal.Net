namespace GalNet.Core.View;

/// <summary>Host-owned audio playback addressed by logical channels.</summary>
public interface IAudioView
{
    /// <summary>Starts an asset on a channel using the entry-defined playback mode and repeat count.</summary>
    void PlayAudio(string channel, string assetId, float volume, string mode, int times);
    void StopAudio(string channel);
    void PauseAudio(string channel);
    void ResumeAudio(string channel);
    void EnqueueAudio(string channel, string assetId, int times);
    /// <summary>Configures host-specific callbacks or actions for queue completion and depletion.</summary>
    void ConfigureAudioQueue(string channel, string onEnd, string onEmpty);
}
