using GalNet.Avalonia.GameView.Page;
using GalNet.Core.View;
using GalNet.Presentation.Defaults.Media;

namespace GalNet.Sample.Avalonia.Presentation;

/// <summary>Sample-specific audio/video service. Replace this in a real client to select another backend.</summary>
internal sealed class SampleMediaViews(GamePageViewModel page, string assetRoot) : IAudioView, IVideoView, IDisposable
{
    private readonly LibVlcAudioController _audio = new(null);

    public void PlayAudio(string channel, string assetId, float volume, string mode, int times)
    {
        _audio.Play(channel, ResolveAssetPath(assetId), volume);
        page.StatusMessage = $"Audio {channel}: {assetId}";
    }

    public void StopAudio(string channel) { _audio.Stop(channel); page.StatusMessage = $"Audio stopped: {channel}"; }
    public void PauseAudio(string channel) { _audio.Pause(channel); page.StatusMessage = $"Audio paused: {channel}"; }
    public void ResumeAudio(string channel) { _audio.Resume(channel); page.StatusMessage = $"Audio resumed: {channel}"; }
    public void EnqueueAudio(string channel, string assetId, int times) => _audio.Enqueue(channel, ResolveAssetPath(assetId));
    public void ConfigureAudioQueue(string channel, string onEnd, string onEmpty) { }
    public void PlayVideo(string assetId) => page.StatusMessage = $"Video requested: {assetId}";
    public void StopVideo() => page.StatusMessage = "Video stopped.";
    public void Dispose() => _audio.Dispose();

    private string ResolveAssetPath(string assetId) => Path.IsPathRooted(assetId) ? assetId : Path.Combine(assetRoot, assetId);
}
