using System.Text.Json;

namespace GalNet.Core.Scene;

public enum AnimationInterpolation
{
    Step,
    Linear,
    CubicHermite
}

/// <summary>Serializable clip-like timeline for float scene properties.</summary>
public sealed class AnimationPlanDefinition
{
    public int FrameRate { get; set; } = 60;
    public int DurationFrames { get; set; }
    public bool Blocking { get; set; }
    public bool Skippable { get; set; }
    public string? BatchId { get; set; }
    public List<AnimationTrackDefinition> Tracks { get; set; } = [];
    public List<AnimationPlanEventDefinition> Events { get; set; } = [];
}

public sealed class AnimationTrackDefinition
{
    public string HandleId { get; set; } = "";
    public string Property { get; set; } = "";
    public List<AnimationKeyframeDefinition> Keys { get; set; } = [];
}

/// <summary>Tangents are values per timeline frame. Interpolation belongs to the segment after this key.</summary>
public sealed class AnimationKeyframeDefinition
{
    public int Frame { get; set; }
    public float Value { get; set; }
    public float InTangent { get; set; }
    public float OutTangent { get; set; }
    public AnimationInterpolation InterpolationToNext { get; set; } = AnimationInterpolation.Linear;
}

public sealed class AnimationPlanEventDefinition
{
    public int Frame { get; set; }
    public string Type { get; set; } = "";
    public Dictionary<string, JsonElement> Parameters { get; set; } = new(StringComparer.Ordinal);
}

public sealed class AnimationPlanPlayResult
{
    public AnimationOutcome Outcome { get; init; }
    public IReadOnlyDictionary<string, AnimationOutcome> TrackOutcomes { get; init; } = new Dictionary<string, AnimationOutcome>();
}

public static class AnimationTrackSampler
{
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
