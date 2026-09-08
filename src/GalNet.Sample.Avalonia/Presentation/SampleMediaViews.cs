using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Core.View;

namespace GalNet.Sample.Avalonia.Presentation;

/// <summary>Sample-specific audio/video service. Replace this in a real client to select another backend.</summary>
internal sealed class SampleMediaViews(GamePageViewModel page) : IAudioView, IVideoView, IDisposable
{
    public void PlayAudio(string channel, string assetId, float volume, string mode, int times)
    {
        page.StatusMessage = $"Audio unavailable in this sample build ({channel}: {assetId}).";
    }

    public void StopAudio(string channel) => page.StatusMessage = $"Audio unavailable in this sample build ({channel}).";
    public void PauseAudio(string channel) => page.StatusMessage = $"Audio unavailable in this sample build ({channel}).";
    public void ResumeAudio(string channel) => page.StatusMessage = $"Audio unavailable in this sample build ({channel}).";
    public void EnqueueAudio(string channel, string assetId, int times) =>
        page.StatusMessage = $"Audio queue unavailable in this sample build ({channel}: {assetId}).";
    public void ConfigureAudioQueue(string channel, string onEnd, string onEmpty) { }
    public void PlayVideo(string assetId) => page.StatusMessage = $"Video requested: {assetId}";
    public void StopVideo() => page.StatusMessage = "Video stopped.";
    public void Dispose() { }
}
