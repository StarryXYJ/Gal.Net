using GalNet.Contracts.Builtins.Scene;

namespace GeneralTest.Scene;

public class TimelineTests
{
    [Test]
    public void Track_sampler_supports_step_and_linear_segments()
    {
        var track = Track(
            new() { Frame = 0, Value = 0, InterpolationToNext = AnimationInterpolation.Step },
            new() { Frame = 10, Value = 1, InterpolationToNext = AnimationInterpolation.Linear },
            new() { Frame = 20, Value = 3 });

        Assert.That(TimelineSampler.Evaluate(track, 5), Is.EqualTo(0));
        Assert.That(TimelineSampler.Evaluate(track, 9.999), Is.EqualTo(0));
        Assert.That(TimelineSampler.Evaluate(track, 10), Is.EqualTo(1), "An authored key wins at its exact frame, including after a stepped segment.");
        Assert.That(TimelineSampler.Evaluate(track, 10.001), Is.GreaterThan(1));
        Assert.That(TimelineSampler.Evaluate(track, 15), Is.EqualTo(2));
    }

    [Test]
    public void Track_sampler_uses_hermite_tangents_and_can_overshoot()
    {
        var track = Track(
            new() { Frame = 0, Value = 0, OutTangent = .5f, InterpolationToNext = AnimationInterpolation.CubicHermite },
            new() { Frame = 10, Value = 1, InTangent = 0 });

        Assert.That(TimelineSampler.Evaluate(track, 5), Is.GreaterThan(1));
    }

    [Test]
    public void Track_sampler_holds_the_last_value_after_the_final_key()
    {
        var track = Track(new() { Frame = 0, Value = 2 }, new() { Frame = 10, Value = 4 });
        Assert.That(TimelineSampler.Evaluate(track, 99), Is.EqualTo(4));
    }

    private static TimelineTrackDefinition Track(params TimelineKeyframeDefinition[] keys) => new()
    {
        HandleId = "hero",
        Property = "transform.x",
        Keys = keys.ToList()
    };
}
