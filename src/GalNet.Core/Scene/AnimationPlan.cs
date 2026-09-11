using System.Text.Json;

namespace GalNet.Core.Scene;

/// <summary>Interpolation used by the segment immediately following a timeline keyframe.</summary>
public enum AnimationInterpolation
{
    Step,
    Linear,
    CubicHermite
}

/// <summary>Serializable clip-like timeline for float scene properties.</summary>
/// <example>
/// A plan at 60 fps with <c>DurationFrames = 30</c> plays for half a second. A track whose
/// keys are `(0, 0)` and `(30, 1)` linearly fades its target property from 0 to 1.
/// </example>
public sealed class AnimationPlanDefinition
{
    /// <summary>Opaque handle for this plan playback; it must be unique while the plan is active.</summary>
    public string PlaybackHandleId { get; set; } = "";
    /// <summary>Timeline sampling frequency in frames per second. Valid authoring values are 1 through 240.</summary>
    public int FrameRate { get; set; } = 60;
    /// <summary>Inclusive timeline end frame. Tracks must start at frame 0 and may not exceed this value.</summary>
    public int DurationFrames { get; set; }
    public bool Blocking { get; set; }
    public bool Skippable { get; set; }
    /// <summary>Optional authoring batch identity used by the presentation's batch-skip behavior.</summary>
    public string? BatchId { get; set; }
    /// <summary>Whether the whole timeline plays once or is restarted after its final frame.</summary>
    public AnimationLoopMode LoopMode { get; set; }
    /// <summary>Property tracks sampled in parallel during playback.</summary>
    public List<AnimationTrackDefinition> Tracks { get; set; } = [];
    /// <summary>Entries dispatched at their declared frames without creating a separate engine checkpoint.</summary>
    public List<AnimationPlanEventDefinition> Events { get; set; } = [];
}

/// <summary>Keyframed values for one property on one active scene instance.</summary>
public sealed class AnimationTrackDefinition
{
    public string HandleId { get; set; } = "";
    public string Property { get; set; } = "";
    /// <summary>Whether keys are absolute property values or relative offsets from the current Replace value.</summary>
    public AnimationBlendMode BlendMode { get; set; }
    /// <summary>Strictly frame-ordered keys; the first key must be at frame 0.</summary>
    public List<AnimationKeyframeDefinition> Keys { get; set; } = [];
}

/// <summary>Tangents are values per timeline frame. Interpolation belongs to the segment after this key.</summary>
public sealed class AnimationKeyframeDefinition
{
    public int Frame { get; set; }
    public float Value { get; set; }
    /// <summary>Incoming derivative in value-per-frame units, used only by cubic Hermite interpolation.</summary>
    public float InTangent { get; set; }
    /// <summary>Outgoing derivative in value-per-frame units, used only by cubic Hermite interpolation.</summary>
    public float OutTangent { get; set; }
    public AnimationInterpolation InterpolationToNext { get; set; } = AnimationInterpolation.Linear;
}

/// <summary>Entry-like event embedded in a timeline and dispatched when its frame is reached.</summary>
public sealed class AnimationPlanEventDefinition
{
    public int Frame { get; set; }
    public string Type { get; set; } = "";
    /// <summary>Structured entry parameters preserved until the event is dispatched.</summary>
    public Dictionary<string, JsonElement> Parameters { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>Terminal plan outcome plus the terminal outcome recorded for each track.</summary>
public sealed class AnimationPlanPlayResult
{
    public AnimationOutcome Outcome { get; init; }
    public IReadOnlyDictionary<string, AnimationOutcome> TrackOutcomes { get; init; } = new Dictionary<string, AnimationOutcome>();
}

/// <summary>Pure keyframe sampler shared by presentation implementations and tests.</summary>
public static class AnimationTrackSampler
{
    /// <summary>Evaluates one validated track at a fractional timeline frame.</summary>
    /// <param name="track">Track whose keys are sampled.</param>
    /// <param name="frame">Frame position, including fractional frames between authored keys.</param>
    /// <returns>The interpolated property value, clamped to the first or last key outside the track range.</returns>
    public static float Evaluate(AnimationTrackDefinition track, double frame)
    {
        var keys = track.Keys;
        if (frame <= keys[0].Frame) return keys[0].Value;
        if (frame >= keys[^1].Frame) return keys[^1].Value;

        var index = 0;
        while (keys[index + 1].Frame < frame) index++;
        var start = keys[index];
        var end = keys[index + 1];
        var span = end.Frame - start.Frame;
        var t = (float)((frame - start.Frame) / span);
        return start.InterpolationToNext switch
        {
            AnimationInterpolation.Step => start.Value,
            AnimationInterpolation.Linear => start.Value + ((end.Value - start.Value) * t),
            AnimationInterpolation.CubicHermite => Hermite(start.Value, start.OutTangent * span, end.Value, end.InTangent * span, t),
            _ => throw new InvalidDataException($"Unsupported key interpolation '{start.InterpolationToNext}'.")
        };
    }

    private static float Hermite(float start, float startTangent, float end, float endTangent, float t)
    {
        var squared = t * t;
        var cubed = squared * t;
        return ((2 * cubed) - (3 * squared) + 1) * start +
               (cubed - (2 * squared) + t) * startTangent +
               ((-2 * cubed) + (3 * squared)) * end +
               (cubed - squared) * endTangent;
    }
}
