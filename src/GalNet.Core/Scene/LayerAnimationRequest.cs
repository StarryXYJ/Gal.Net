namespace GalNet.Core.Scene;

/// <summary>One Replace-mode interpolation of an active Layer property.</summary>
public sealed class LayerAnimationRequest
{
    public string HandleId { get; set; } = "";
    public string Property { get; set; } = "";
    public float? From { get; set; }
    public float To { get; set; }
    public double DurationSeconds { get; set; }
    public AnimationEasing Easing { get; set; } = AnimationEasing.Linear;
    public bool Blocking { get; set; }
    public bool Skippable { get; set; }
    public string? BatchId { get; set; }
}

public enum AnimationEasing { Linear, Step, EaseIn, EaseOut, EaseInOut }
public enum AnimationOutcome { Completed, Skipped, Replaced }
