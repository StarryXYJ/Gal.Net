namespace GalNet.Core.Scene;

/// <summary>One Replace-mode interpolation of an active scene-instance property.</summary>
public sealed class AnimationRequest
{
    public string HandleId { get; set; } = "";
    public string Property { get; set; } = "";
    public float? From { get; set; }
    public float To { get; set; }
    public double DurationSeconds { get; set; }
    public IAnimationCurve Curve { get; set; } = AnimationCurves.Linear;
    public bool Blocking { get; set; }
    public bool Skippable { get; set; }
    public string? BatchId { get; set; }
}

public enum AnimationOutcome { Completed, Skipped, Replaced }
