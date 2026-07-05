namespace GalNet.Core.Services;

/// <summary>
/// Audio playback service.
///
/// Channel convention:
///   0 = BGM      1 = BGM2      2 = Voice      3..N = SFX (count configurable)
/// </summary>
public interface IAudioService
{
    void Play(int channel, string assetId, float volume = 1f, string mode = "once", int times = 1);
    void Stop(int channel);
    void Pause(int channel);
    void Resume(int channel);
    void Enqueue(int channel, string assetId, int times = 1);

    float GetVolume(int channel);
    void SetVolume(int channel, float volume);
    void SetMasterVolume(float volume);
}
