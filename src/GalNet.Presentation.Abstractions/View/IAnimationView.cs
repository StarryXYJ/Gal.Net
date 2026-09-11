using GalNet.Core.Scene;

namespace GalNet.Core.View;

/// <summary>Presentation port for immediate property animation and keyframe timeline playback.</summary>
public interface IAnimationView
{
    /// <summary>Plays one immediate property interpolation.</summary>
    /// <param name="request">Validated target, timing, curve and playback semantics.</param>
    /// <param name="ct">Cancels the presentation operation.</param>
    /// <returns>Whether playback completed, was skipped, or was replaced.</returns>
    Task<AnimationOutcome> AnimateAsync(AnimationRequest request, CancellationToken ct);
    /// <summary>Plays all property tracks and embedded events in a keyframe timeline.</summary>
    /// <param name="plan">Validated plan definition with its unique playback handle.</param>
    /// <param name="ct">Cancels the presentation operation.</param>
    /// <returns>The plan outcome and each track's terminal outcome.</returns>
    Task<AnimationPlanPlayResult> PlayAnimationPlanAsync(AnimationPlanDefinition plan, CancellationToken ct);
    /// <summary>Requests immediate completion of the active playback with the supplied handle.</summary>
    /// <param name="playbackHandleId">Opaque handle supplied by the animate entry or plan.</param>
    /// <returns><see langword="true"/> when an active playback accepted the request.</returns>
    bool CompleteAnimationImmediately(string playbackHandleId);
    /// <summary>Skips the next active skippable playback batch according to presentation ordering.</summary>
    /// <returns><see langword="true"/> when at least one playback was skipped.</returns>
    bool SkipAnimationBatch();
}
