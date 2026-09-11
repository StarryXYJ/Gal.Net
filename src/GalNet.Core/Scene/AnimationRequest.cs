namespace GalNet.Core.Scene;

/// <summary>One Replace-mode interpolation of an active scene-instance property.</summary>
public sealed class AnimationRequest
{
    /// <summary>Opaque handle for this playback, distinct from the animated target handle.</summary>
    public string PlaybackHandleId { get; set; } = "";
    public string HandleId { get; set; } = "";
    public string Property { get; set; } = "";
    /// <summary>Optional explicit start value. When absent, the presentation reads the displayed value at playback start.</summary>
    public float? From { get; set; }
    /// <summary>Validated value written to Runtime state after a completed or skipped playback.</summary>
    public float To { get; set; }
    public double DurationSeconds { get; set; }
    public IAnimationCurve Curve { get; set; } = AnimationCurves.Linear;
    public bool Blocking { get; set; }
    /// <summary>Whether an advance request may complete this playback or its batch early.</summary>
    public bool Skippable { get; set; }
    /// <summary>Optional authoring batch identity used to skip related eligible playbacks together.</summary>
    public string? BatchId { get; set; }
    /// <summary>Whether the request plays once or repeats until an explicit stop is requested.</summary>
    public AnimationLoopMode LoopMode { get; set; }
}

/// <summary>Terminal result reported by a presentation animation operation.</summary>
public enum AnimationOutcome { Completed, Skipped, Replaced }

/// <summary>Controls whether an animation request performs one iteration or repeats indefinitely.</summary>
public enum AnimationLoopMode { Once, Loop }

/// <summary>Defines how a looped playback responds to an explicit stop request.</summary>
public enum AnimationStopMode { AfterIteration, CompleteImmediately }

/// <summary>Ephemeral runtime instance that owns a single animation playback lifecycle.</summary>
public sealed class AnimationPlaybackInstance : ISceneInstance
{
    private readonly object _gate = new();
    private AnimationStopMode? _requestedStop;

    /// <summary>Stable playback handle used by stop commands and to prevent duplicate active playbacks.</summary>
    public string Id { get; init; } = "";
    public AnimationLoopMode LoopMode { get; init; }

    /// <summary>Most recent requested stop mode, or <see langword="null"/> while playback may continue.</summary>
    public AnimationStopMode? RequestedStop
    {
        get { lock (_gate) return _requestedStop; }
    }

    /// <summary>
    /// Requests that this playback stop. A later immediate request wins over an earlier orderly request.
    /// </summary>
    /// <param name="mode">Whether to stop after the current iteration or finish immediately.</param>
    public void RequestStop(AnimationStopMode mode)
    {
        lock (_gate)
        {
            // Completion-at-end wins if a later command escalates an orderly stop.
            if (_requestedStop is null || mode == AnimationStopMode.CompleteImmediately)
                _requestedStop = mode;
        }
    }
}
