namespace GalNet.Core.View;

public interface IAudioView
{
    void PlayAudio(string channel, string assetId, float volume, string mode, int times);
    void StopAudio(string channel);
    void PauseAudio(string channel);
    void ResumeAudio(string channel);
    void EnqueueAudio(string channel, string assetId, int times);
    void ConfigureAudioQueue(string channel, string onEnd, string onEmpty);
}
