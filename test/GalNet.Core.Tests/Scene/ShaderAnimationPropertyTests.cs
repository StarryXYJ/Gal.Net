using GalNet.Core.Scene;

namespace GeneralTest.Scene;

public sealed class ShaderAnimationPropertyTests
{
    [Test]
    public void ParserAndSharedConverterRetainOnlySupportedAnimatableScalars()
    {
        var parsed = ShaderEffectMetadataParser.Parse(new("program"), """
            /*
            @gal.effect v=1
            @input source
            @targets layer,scenePost
            @param amount
              uniform: uAmount
              type: float
              default: 0.25
              range: 0..1
            @param count
              uniform: uCount
              type: int
              default: 2
            @param disabled
              uniform: uDisabled
              type: float
              animatable: false
            @param tint
              uniform: uTint
              type: color
            */
            """);
        Assert.That(parsed.Success, Is.True, string.Join(";", parsed.Diagnostics));
        var properties = ShaderEffectAnimationProperties.Create(parsed.Descriptor!);
        Assert.That(properties.Select(property => property.Name), Is.EqualTo(new[] { "amount", "count" }));
        Assert.That(properties[0], Is.EqualTo(new AnimatableProperty("amount", AnimationValueKind.Float, 0, 1, .25f)));
        Assert.That(properties[1].DefaultValue, Is.EqualTo(2));
    }
}
