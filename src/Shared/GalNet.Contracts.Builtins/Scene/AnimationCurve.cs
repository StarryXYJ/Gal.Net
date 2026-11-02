namespace GalNet.Contracts.Builtins.Scene;

/// <summary>Named easing curves available to immediate <c>animate</c> entries.</summary>
public enum BuiltinAnimationCurve
{
    Linear,
    Step,
    QuadEaseIn,
    QuadEaseOut,
    QuadEaseInOut,
    CubicEaseIn,
    CubicEaseOut,
    CubicEaseInOut,
    QuartEaseIn,
    QuartEaseOut,
    QuartEaseInOut,
    QuintEaseIn,
    QuintEaseOut,
    QuintEaseInOut,
    SineEaseIn,
    SineEaseOut,
    SineEaseInOut,
    ExpoEaseIn,
    ExpoEaseOut,
    ExpoEaseInOut,
    CircEaseIn,
    CircEaseOut,
    CircEaseInOut,
    BackEaseIn,
    BackEaseOut,
    BackEaseInOut,
    ElasticEaseIn,
    ElasticEaseOut,
    ElasticEaseInOut,
    BounceEaseIn,
    BounceEaseOut,
    BounceEaseInOut,
}

/// <summary>Maps normalized time to interpolation progress, which may overshoot for Back and Elastic curves.</summary>
public interface IAnimationCurve
{
    float Evaluate(float normalizedTime);
}

public static class AnimationCurves
{
    public static readonly IAnimationCurve Linear = Create(BuiltinAnimationCurve.Linear);

    /// <summary>Creates a stateless evaluator that clamps input time to [0, 1], preserving exact endpoints.</summary>
    /// <exception cref="InvalidDataException">The enum value is not a supported curve.</exception>
    public static IAnimationCurve Create(BuiltinAnimationCurve curve)
    {
        if (!Enum.IsDefined(curve))
            throw new InvalidDataException($"Unsupported built-in animation curve '{curve}'.");
        return new BuiltinCurve(curve);
    }

    private sealed class BuiltinCurve(BuiltinAnimationCurve kind) : IAnimationCurve
    {
        public float Evaluate(float normalizedTime)
        {
            var t = Math.Clamp(normalizedTime, 0, 1);
            if (t <= 0 || t >= 1) return t;
            return kind switch
            {
                BuiltinAnimationCurve.Linear => t,
                BuiltinAnimationCurve.Step => 0,
                BuiltinAnimationCurve.QuadEaseIn => EvaluateIn(BuiltinAnimationCurve.QuadEaseIn, t),
                BuiltinAnimationCurve.QuadEaseOut => EvaluateOut(BuiltinAnimationCurve.QuadEaseIn, t),
                BuiltinAnimationCurve.QuadEaseInOut => EvaluateInOut(BuiltinAnimationCurve.QuadEaseIn, t),
                BuiltinAnimationCurve.CubicEaseIn => EvaluateIn(BuiltinAnimationCurve.CubicEaseIn, t),
                BuiltinAnimationCurve.CubicEaseOut => EvaluateOut(BuiltinAnimationCurve.CubicEaseIn, t),
                BuiltinAnimationCurve.CubicEaseInOut => EvaluateInOut(BuiltinAnimationCurve.CubicEaseIn, t),
                BuiltinAnimationCurve.QuartEaseIn => EvaluateIn(BuiltinAnimationCurve.QuartEaseIn, t),
                BuiltinAnimationCurve.QuartEaseOut => EvaluateOut(BuiltinAnimationCurve.QuartEaseIn, t),
                BuiltinAnimationCurve.QuartEaseInOut => EvaluateInOut(BuiltinAnimationCurve.QuartEaseIn, t),
                BuiltinAnimationCurve.QuintEaseIn => EvaluateIn(BuiltinAnimationCurve.QuintEaseIn, t),
                BuiltinAnimationCurve.QuintEaseOut => EvaluateOut(BuiltinAnimationCurve.QuintEaseIn, t),
                BuiltinAnimationCurve.QuintEaseInOut => EvaluateInOut(BuiltinAnimationCurve.QuintEaseIn, t),
                BuiltinAnimationCurve.SineEaseIn => EvaluateIn(BuiltinAnimationCurve.SineEaseIn, t),
                BuiltinAnimationCurve.SineEaseOut => EvaluateOut(BuiltinAnimationCurve.SineEaseIn, t),
                BuiltinAnimationCurve.SineEaseInOut => EvaluateInOut(BuiltinAnimationCurve.SineEaseIn, t),
                BuiltinAnimationCurve.ExpoEaseIn => EvaluateIn(BuiltinAnimationCurve.ExpoEaseIn, t),
                BuiltinAnimationCurve.ExpoEaseOut => EvaluateOut(BuiltinAnimationCurve.ExpoEaseIn, t),
                BuiltinAnimationCurve.ExpoEaseInOut => EvaluateInOut(BuiltinAnimationCurve.ExpoEaseIn, t),
                BuiltinAnimationCurve.CircEaseIn => EvaluateIn(BuiltinAnimationCurve.CircEaseIn, t),
                BuiltinAnimationCurve.CircEaseOut => EvaluateOut(BuiltinAnimationCurve.CircEaseIn, t),
                BuiltinAnimationCurve.CircEaseInOut => EvaluateInOut(BuiltinAnimationCurve.CircEaseIn, t),
                BuiltinAnimationCurve.BackEaseIn => EvaluateIn(BuiltinAnimationCurve.BackEaseIn, t),
                BuiltinAnimationCurve.BackEaseOut => EvaluateOut(BuiltinAnimationCurve.BackEaseIn, t),
                BuiltinAnimationCurve.BackEaseInOut => EvaluateInOut(BuiltinAnimationCurve.BackEaseIn, t),
                BuiltinAnimationCurve.ElasticEaseIn => EvaluateIn(BuiltinAnimationCurve.ElasticEaseIn, t),
                BuiltinAnimationCurve.ElasticEaseOut => EvaluateOut(BuiltinAnimationCurve.ElasticEaseIn, t),
                BuiltinAnimationCurve.ElasticEaseInOut => EvaluateInOut(BuiltinAnimationCurve.ElasticEaseIn, t),
                BuiltinAnimationCurve.BounceEaseIn => EvaluateIn(BuiltinAnimationCurve.BounceEaseIn, t),
                BuiltinAnimationCurve.BounceEaseOut => EvaluateOut(BuiltinAnimationCurve.BounceEaseIn, t),
                BuiltinAnimationCurve.BounceEaseInOut => EvaluateInOut(BuiltinAnimationCurve.BounceEaseIn, t),
                _ => throw new InvalidDataException($"Unsupported built-in animation curve '{kind}'.")
            };
        }
    }

    private static float EvaluateOut(BuiltinAnimationCurve easeIn, float t) => 1 - EvaluateIn(easeIn, 1 - t);

    private static float EvaluateInOut(BuiltinAnimationCurve easeIn, float t) => t < .5f
        ? EvaluateIn(easeIn, 2 * t) / 2
        : 1 - EvaluateIn(easeIn, 2 - 2 * t) / 2;

    private static float EvaluateIn(BuiltinAnimationCurve kind, float t) => kind switch
    {
        BuiltinAnimationCurve.QuadEaseIn => t * t,
        BuiltinAnimationCurve.CubicEaseIn => t * t * t,
        BuiltinAnimationCurve.QuartEaseIn => t * t * t * t,
        BuiltinAnimationCurve.QuintEaseIn => t * t * t * t * t,
        BuiltinAnimationCurve.SineEaseIn => 1 - MathF.Cos(t * MathF.PI / 2),
        BuiltinAnimationCurve.ExpoEaseIn => t <= 0 ? 0 : MathF.Pow(2, 10 * t - 10),
        BuiltinAnimationCurve.CircEaseIn => 1 - MathF.Sqrt(Math.Max(0, 1 - t * t)),
        BuiltinAnimationCurve.BackEaseIn => 2.70158f * t * t * t - 1.70158f * t * t,
        BuiltinAnimationCurve.ElasticEaseIn => t <= 0 || t >= 1 ? t
            : -MathF.Pow(2, 10 * t - 10) * MathF.Sin((10 * t - 10.75f) * (2 * MathF.PI / 3)),
        BuiltinAnimationCurve.BounceEaseIn => 1 - BounceOut(1 - t),
        _ => throw new InvalidDataException($"Unsupported ease-in curve '{kind}'.")
    };

    private static float BounceOut(float t)
    {
        const float n = 7.5625f;
        const float d = 2.75f;
        if (t < 1 / d) return n * t * t;
        if (t < 2 / d) { t -= 1.5f / d; return n * t * t + .75f; }
        if (t < 2.5f / d) { t -= 2.25f / d; return n * t * t + .9375f; }
        t -= 2.625f / d;
        return n * t * t + .984375f;
    }
}
