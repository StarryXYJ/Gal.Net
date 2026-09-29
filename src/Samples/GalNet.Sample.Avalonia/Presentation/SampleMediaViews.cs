using GalNet.Avalonia.GameView.ViewModels;
using Avalonia.Threading;

namespace GalNet.Sample.Avalonia.Presentation;

/// <summary>Sample-specific audio/video service. Replace this in a real client to select another backend.</summary>
internal sealed class SampleMediaViews(GamePageViewModel page) : IDisposable
{
    public void PlayAudio(string channel, string assetId, float volume, string mode, int times)
    {
        SetStatus($"Audio unavailable in this sample build ({channel}: {assetId}).");
    }

    public void StopAudio(string channel) => SetStatus($"Audio unavailable in this sample build ({channel}).");
    public void PauseAudio(string channel) => SetStatus($"Audio unavailable in this sample build ({channel}).");
    public void ResumeAudio(string channel) => SetStatus($"Audio unavailable in this sample build ({channel}).");
    public void EnqueueAudio(string channel, string assetId, int times) =>
        SetStatus($"Audio queue unavailable in this sample build ({channel}: {assetId}).");
    public void ConfigureAudioQueue(string channel, string onEnd, string onEmpty) { }
    public void PlayVideo(string assetId) => SetStatus($"Video requested: {assetId}");
    public void StopVideo() => SetStatus("Video stopped.");
    public void Dispose() { }

    private void SetStatus(string message)
    {
        if (Dispatcher.UIThread.CheckAccess()) page.StatusMessage = message;
        else Dispatcher.UIThread.Post(() => page.StatusMessage = message);
    }
}
