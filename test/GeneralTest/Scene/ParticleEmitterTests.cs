using System.Text.Json;
using Avalonia;
using GalNet.Core.Scene;
using GalNet.Rendering.Scene;
using SkiaSharp;
using RenderParticleEmitter = GalNet.Rendering.Scene.ParticleEmitter;

namespace GeneralTest.Scene;

public class ParticleEmitterDefinitionTests
{
    [Test]
    public void LegacyJsonKeepsFlatValuesAndLegacyShape()
    {
        var definition = ParticleEmitterDefinition.FromJson("""
        {
          "particleTexture": "snowflake",
          "emissionRate": 72,
          "maxParticles": 240
        }
        """);

        Assert.Multiple(() =>
        {
            Assert.That(definition.Version, Is.EqualTo(1));
            Assert.That(definition.EmissionRate, Is.EqualTo(72));
            Assert.That(definition.EffectiveEmission.RateOverTime, Is.EqualTo(72));
            Assert.That(definition.Emission, Is.Null);
            Assert.That(definition.Shape, Is.Null);
        });
    }

    [Test]
    public void VersionTwoJsonParsesEmissionBurstsAndShape()
    {
        var definition = ParticleEmitterDefinition.FromJson("""
        {
          "version": 2,
          "particleTexture": "spark",
          "maxParticles": 80,
          "emission": {
            "rateOverTime": 12,
            "bursts": [
              { "time": 0.5, "count": 20 },
              { "time": 0, "count": 8 }
            ]
          },
          "shape": {
            "type": "line",
            "x": 10,
            "y": 20,
            "endX": 90,
            "endY": 40
          }
        }
        """);

        Assert.Multiple(() =>
        {
            Assert.That(definition.Version, Is.EqualTo(2));
            Assert.That(definition.EmissionRate, Is.EqualTo(12));
            Assert.That(definition.Emission!.Bursts, Is.EqualTo(new[] { new ParticleBurst(0, 8), new ParticleBurst(.5f, 20) }));
            Assert.That(definition.Shape, Is.EqualTo(new ParticleShapeDefinition(ParticleShapeKind.Line, 10, 20, EndX: 90, EndY: 40)));
        });
    }

    [Test]
    public void VersionTwoDefinitionRoundTripsThroughSnapshotJson()
    {
        var source = new ParticleEmitterDefinition(
            "spark",
            Version: 2,
            Emission: new ParticleEmissionDefinition(0, [new ParticleBurst(0, 24)]),
            Shape: new ParticleShapeDefinition(ParticleShapeKind.Circle, 50, 60, Radius: 32));

        var restored = JsonSerializer.Deserialize<ParticleEmitterDefinition>(JsonSerializer.Serialize(source));

        Assert.Multiple(() =>
        {
            Assert.That(restored!.ParticleTexture, Is.EqualTo(source.ParticleTexture));
            Assert.That(restored.Version, Is.EqualTo(2));
            Assert.That(restored.EffectiveEmission.RateOverTime, Is.Zero);
            Assert.That(restored.EffectiveEmission.Bursts, Is.EqualTo(source.EffectiveEmission.Bursts));
            Assert.That(restored.Shape, Is.EqualTo(source.Shape));
        });
    }
}

public class ParticleEmitterSimulationTests
{
    [Test]
    public void BurstFiresOnceWhenItsTimeIsCrossedAndHonorsMaximum()
    {
        using var emitter = new RenderParticleEmitter("burst", null, new ParticleEmitterDefinition(
            "",
            MaxParticles: 2,
            InitialVelocityX: 0,
            InitialVelocityY: 0,
            Noise: 0,
            ParticleLifetime: 10,
            Version: 2,
            Emission: new ParticleEmissionDefinition(0, [new ParticleBurst(.5f, 8)]),
            Shape: new ParticleShapeDefinition(ParticleShapeKind.Point, 20, 30)), 0);
        var size = new SKSize(100, 100);

        emitter.Update(new SceneFrameContext(TimeSpan.FromSeconds(.49), TimeSpan.FromSeconds(.49), size));
        Assert.That(emitter.ActiveParticleCount, Is.Zero);

        emitter.Update(new SceneFrameContext(TimeSpan.FromSeconds(.51), TimeSpan.FromSeconds(.02), size));
        emitter.Update(new SceneFrameContext(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(.49), size));

        Assert.That(emitter.ActiveParticleCount, Is.EqualTo(2));
    }

    [Test]
    public void PointShapeRendersBurstAtAuthoredPosition()
    {
        using var emitter = new RenderParticleEmitter("point", null, new ParticleEmitterDefinition(
            "",
            InitialVelocityX: 0,
            InitialVelocityY: 0,
            Noise: 0,
            ParticleLifetime: 10,
            Version: 2,
            Emission: new ParticleEmissionDefinition(0, [new ParticleBurst(0, 1)]),
            Shape: new ParticleShapeDefinition(ParticleShapeKind.Point, 20, 30)), 0);
        emitter.Update(new SceneFrameContext(TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1), new SKSize(100, 100)));

        using var frame = SceneRenderPipeline.Render(SceneRenderPlan.Empty, [], [emitter], new Size(100, 100));

        Assert.Multiple(() =>
        {
            Assert.That(frame.GetPixel(20, 30), Is.Not.EqualTo(SKColors.Black));
            Assert.That(frame.GetPixel(75, 75), Is.EqualTo(SKColors.Black));
        });
    }
}
