using GalNet.Core.Scene;

namespace GalNet.Presentation.Abstractions.View;

/// <summary>Presentation operations used by animation primitive instances.</summary>
public interface IAnimationPresenter
{
    Task<AnimationOutcome> AnimateAsync(AnimationRequest request, CancellationToken cancellationToken);
    /// <summary>Await beforeFrame with monotonically increasing elapsed frames before sampling each frame.
    /// The callback owns event dispatch; the presenter owns timing and must publish the terminal frame before normal completion.</summary>
    Task<TimelinePlayResult> PlayTimelineAsync(TimelineDefinition timeline, CancellationToken cancellationToken, Func<double, Task>? beforeFrame = null);
    bool CompleteAnimationImmediately(string playbackHandleId);
}
