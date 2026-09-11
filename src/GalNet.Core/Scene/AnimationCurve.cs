namespace GalNet.Core.Scene;

/// <summary>Named easing curves available to immediate <c>animate</c> entries.</summary>
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

    /// <summary>Creates a stateless evaluator for the selected built-in curve.</summary>
    /// <param name="curve">Named curve selected by authored content.</param>
    /// <returns>An evaluator that clamps input time to the closed <c>[0, 1]</c> interval.</returns>
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
