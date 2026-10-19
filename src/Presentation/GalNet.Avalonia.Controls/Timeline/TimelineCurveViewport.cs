using GalNet.Core.Scene;

namespace GalNet.Game.Controls.Timeline;

public sealed record TimelineCurveGesture(int FrameDelta = 0, float ValueDelta = 0, float? Tangent = null, bool Incoming = false);

/// <summary>Numeric value camera, independent of author data and history.</summary>
public sealed class TimelineCurveViewport
{
    public double Minimum { get; private set; } = -1;
    public double Maximum { get; private set; } = 1;
    public const double Height = 176;
    public const double Padding = 16;
    public double Range => Maximum - Minimum;
    public double Y(double value) => Padding + (Maximum - value) / Range * (Height - 2 * Padding);
    public double ValueAt(double y) => Maximum - (y - Padding) / (Height - 2 * Padding) * Range;
    public void Pan(double delta) => Set(Minimum + delta, Maximum + delta);
    public void Zoom(double factor, double anchor) => Set(anchor + (Minimum - anchor) * factor, anchor + (Maximum - anchor) * factor);
    private void Set(double minimum, double maximum)
    {
        if (double.IsFinite(minimum) && double.IsFinite(maximum) && maximum - minimum is > 1e-12 and < 1e40)
        { Minimum = minimum; Maximum = maximum; }
    }
    public void Fit(IReadOnlyList<TimelineCanvasKey> keys)
    {
        if (keys.Count == 0) return;
        var track = Track(keys); var values = keys.Select(key => (double)key.Value).ToList();
        // Analytic extrema of each cubic, rather than hiding overshoot with a sampled fit.
        for (var index = 0; index + 1 < keys.Count; index++)
        {
            var left = keys[index]; var right = keys[index + 1];
            if (left.Interpolation != AnimationInterpolation.CubicHermite) continue;
            var span = (double)right.Frame - left.Frame; var m0 = left.OutTangent * span; var m1 = right.InTangent * span;
            var a = 2 * left.Value - 2d * right.Value + m0 + m1;
            var b = -3d * left.Value + 3d * right.Value - 2 * m0 - m1; var c = m0;
            var discriminant = 4 * b * b - 12 * a * c;
            var roots = Math.Abs(a) < 1e-20 ? new[] { Math.Abs(b) < 1e-20 ? -1 : -c / (2 * b) }
                : discriminant < 0 ? [] : new[] { (-2 * b - Math.Sqrt(discriminant)) / (6 * a), (-2 * b + Math.Sqrt(discriminant)) / (6 * a) };
            foreach (var t in roots.Where(t => t is > 0 and < 1))
            { var value = TimelineSampler.Evaluate(track, left.Frame + t * span); if (float.IsFinite(value)) values.Add(value); }
        }
        var minimum = values.Min(); var maximum = values.Max(); var margin = Math.Max(1e-4, Math.Max(maximum - minimum, Math.Max(1, Math.Abs(minimum)) * .1) * .15);
        Set(minimum - margin, maximum + margin);
    }
    public static TimelineTrackDefinition Track(IEnumerable<TimelineCanvasKey> keys) => new()
    {
        Keys = keys.Select(key => new TimelineKeyframeDefinition { Frame = key.Frame, Value = key.Value, InTangent = key.InTangent, OutTangent = key.OutTangent, InterpolationToNext = key.Interpolation }).OrderBy(key => key.Frame).ToList()
    };
}
