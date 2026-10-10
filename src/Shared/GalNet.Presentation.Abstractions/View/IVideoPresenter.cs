namespace GalNet.Presentation.Abstractions.View;

/// <summary>Platform video playback whose decoded frames participate in scene layers.</summary>
public interface IVideoPresenter
{
    Task PlayVideoAsync(VideoRenderRequest request, CancellationToken cancellationToken);
    void AdvanceVideo(string playbackId);
    void StopVideo(string handleId);
}

public sealed record VideoRenderRequest(string PlaybackId, LayerRenderRequest Layer, IReadOnlyList<double> SkipPoints);
