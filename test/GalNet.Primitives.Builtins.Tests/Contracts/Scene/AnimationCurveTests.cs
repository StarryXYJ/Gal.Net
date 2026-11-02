using GalNet.Contracts.Builtins.Scene;

namespace GeneralTest.Scene;

public sealed class AnimationCurveTests
{
    private static IEnumerable<BuiltinAnimationCurve> Curves => Enum.GetValues<BuiltinAnimationCurve>();

    [TestCaseSource(nameof(Curves))]
    public void CurvesHaveExactEndpointsClampTimeAndRemainFinite(BuiltinAnimationCurve kind)
    {
        var curve = AnimationCurves.Create(kind);
        Assert.Multiple(() =>
        {
            Assert.That(curve.Evaluate(-1), Is.Zero);
            Assert.That(curve.Evaluate(0), Is.Zero);
            Assert.That(curve.Evaluate(1), Is.EqualTo(1));
            Assert.That(curve.Evaluate(2), Is.EqualTo(1));
        });
        for (var i = 0; i <= 1000; i++)
            Assert.That(float.IsFinite(curve.Evaluate(i / 1000f)), Is.True, $"{kind} at {i}");
    }

    [TestCase(BuiltinAnimationCurve.Linear, .25f)]
    [TestCase(BuiltinAnimationCurve.Step, 0f)]
    [TestCase(BuiltinAnimationCurve.QuadEaseIn, .0625f)]
    [TestCase(BuiltinAnimationCurve.CubicEaseIn, .015625f)]
    [TestCase(BuiltinAnimationCurve.QuartEaseIn, .00390625f)]
    [TestCase(BuiltinAnimationCurve.QuintEaseIn, .0009765625f)]
    [TestCase(BuiltinAnimationCurve.SineEaseIn, .07612047f)]
    [TestCase(BuiltinAnimationCurve.ExpoEaseIn, .005524272f)]
    [TestCase(BuiltinAnimationCurve.CircEaseIn, .03175416f)]
    [TestCase(BuiltinAnimationCurve.BackEaseIn, -.064136565f)]
    [TestCase(BuiltinAnimationCurve.ElasticEaseIn, -.005524272f)]
    [TestCase(BuiltinAnimationCurve.BounceEaseIn, .02734375f)]
    public void QuarterTimeMatchesReferenceValues(BuiltinAnimationCurve kind, float expected) =>
        Assert.That(AnimationCurves.Create(kind).Evaluate(.25f), Is.EqualTo(expected).Within(.000001f));

    [TestCase("Quad")]
    [TestCase("Cubic")]
    [TestCase("Quart")]
    [TestCase("Quint")]
    [TestCase("Sine")]
    [TestCase("Expo")]
    [TestCase("Circ")]
    [TestCase("Back")]
    [TestCase("Elastic")]
    [TestCase("Bounce")]
    public void DirectionsMirrorAndJoinAtTheMidpoint(string family)
    {
        var easeIn = AnimationCurves.Create(Enum.Parse<BuiltinAnimationCurve>(family + "EaseIn"));
        var easeOut = AnimationCurves.Create(Enum.Parse<BuiltinAnimationCurve>(family + "EaseOut"));
        var easeInOut = AnimationCurves.Create(Enum.Parse<BuiltinAnimationCurve>(family + "EaseInOut"));
        Assert.That(easeInOut.Evaluate(.5f), Is.EqualTo(.5f).Within(.000001f));
        for (var i = 0; i <= 100; i++)
        {
            var t = i / 100f;
            Assert.That(easeOut.Evaluate(t), Is.EqualTo(1 - easeIn.Evaluate(1 - t)).Within(.000001f));
            Assert.That(easeInOut.Evaluate(t), Is.EqualTo(1 - easeInOut.Evaluate(1 - t)).Within(.000002f));
        }
    }

    [TestCaseSource(nameof(Curves))]
    public void OnlyBackAndElasticOvershootAndBounceRemainsBounded(BuiltinAnimationCurve kind)
    {
        var curve = AnimationCurves.Create(kind);
        var values = Enumerable.Range(0, 1001).Select(i => curve.Evaluate(i / 1000f)).ToArray();
        var overshoots = kind.ToString().StartsWith("Back", StringComparison.Ordinal)
            || kind.ToString().StartsWith("Elastic", StringComparison.Ordinal);
        Assert.That(values.Any(value => value < 0 || value > 1), Is.EqualTo(overshoots));
        if (!overshoots && !kind.ToString().StartsWith("Bounce", StringComparison.Ordinal))
            for (var i = 1; i < values.Length; i++)
                Assert.That(values[i], Is.GreaterThanOrEqualTo(values[i - 1]));
    }

    [Test]
    public void UndefinedCurveIsRejected() =>
        Assert.That(() => AnimationCurves.Create((BuiltinAnimationCurve)999), Throws.TypeOf<InvalidDataException>());
}
