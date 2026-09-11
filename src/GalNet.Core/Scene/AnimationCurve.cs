namespace GalNet.Core.Scene;

public enum BuiltinAnimationCurve
{
    Linear,
    Step,
    EaseIn,
    EaseOut,
    EaseInOut
}

/// <summary>A built-in curve evaluates normalized time into an interpolation progress value.</summary>
public interface IAnimationCurve
{
    float Evaluate(float normalizedTime);
}

public static class AnimationCurves
{
    public static readonly IAnimationCurve Linear = Create(BuiltinAnimationCurve.Linear);

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
            return kind switch
            {
                BuiltinAnimationCurve.Step => t >= 1 ? 1 : 0,
                BuiltinAnimationCurve.EaseIn => t * t,
                BuiltinAnimationCurve.EaseOut => 1 - ((1 - t) * (1 - t)),
                BuiltinAnimationCurve.EaseInOut => t < .5f ? 2 * t * t : 1 - (MathF.Pow(-2 * t + 2, 2) / 2),
                _ => t
            };
        }
    }
}
