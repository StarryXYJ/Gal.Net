namespace GalNet.Core.View;

/// <summary>Host-owned single-video playback surface.</summary>
public interface IVideoView
{
    void PlayVideo(string assetId);
    void StopVideo();
}
