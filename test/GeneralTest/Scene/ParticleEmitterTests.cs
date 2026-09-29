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
    public void PlayJsonUsesRateAndExplicitShape()
    {
        var definition = ParticleEmitterDefinition.FromJson("""
        {
          "particleTexture": "snowflake",
          "rate": 72,
          "maxParticles": 240,
          "shape": { "type": "box", "x": 640, "y": -12, "width": 1280 }
        }
        """);

        Assert.Multiple(() =>
        {
            Assert.That(definition.EmissionRate, Is.EqualTo(72));
            Assert.That(definition.Shape, Is.EqualTo(new ParticleShapeDefinition(ParticleShapeKind.Box, 640, -12, 1280)));
        });
    }

    [Test]
    public void JsonParsesLineShape()
    {
        var definition = ParticleEmitterDefinition.FromJson("""
        {
          "particleTexture": "spark",
          "maxParticles": 80,
          "rate": 12,
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
            Assert.That(definition.EmissionRate, Is.EqualTo(12));
            Assert.That(definition.Shape, Is.EqualTo(new ParticleShapeDefinition(ParticleShapeKind.Line, 10, 20, EndX: 90, EndY: 40)));
        });
    }

    [Test]
    public void DefinitionRoundTripsThroughSnapshotJson()
    {
        var source = new ParticleEmitterDefinition(
            "spark",
            EmissionRate: 0,
            Shape: new ParticleShapeDefinition(ParticleShapeKind.Circle, 50, 60, Radius: 32));

        var restored = JsonSerializer.Deserialize<ParticleEmitterDefinition>(JsonSerializer.Serialize(source));

        Assert.Multiple(() =>
        {
            Assert.That(restored!.ParticleTexture, Is.EqualTo(source.ParticleTexture));
            Assert.That(restored.EmissionRate, Is.Zero);
            Assert.That(restored.Shape, Is.EqualTo(source.Shape));
        });
    }
}

public class ParticleEmitterSimulationTests
{
    [Test]
    public void BurstFiresOnceOnFirstUpdateAndHonorsMaximum()
    {
        using var emitter = new RenderParticleEmitter("burst", null, new ParticleEmitterDefinition(
            "",
            MaxParticles: 2,
            InitialVelocityX: 0,
            InitialVelocityY: 0,
            Noise: 0,
            ParticleLifetime: 10,
            Shape: new ParticleShapeDefinition(ParticleShapeKind.Point, 20, 30)), 0, initialBurstCount: 8);
        var size = new SKSize(100, 100);

        emitter.Update(new SceneFrameContext(TimeSpan.FromSeconds(.02), TimeSpan.FromSeconds(.02), size));
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
            Shape: new ParticleShapeDefinition(ParticleShapeKind.Point, 20, 30)), 0, initialBurstCount: 1);
        emitter.Update(new SceneFrameContext(TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1), new SKSize(100, 100)));

        using var frame = SceneRenderPipeline.Render(SceneRenderPlan.Empty, [], [emitter], new Size(100, 100));

        Assert.Multiple(() =>
        {
            Assert.That(frame.GetPixel(20, 30), Is.Not.EqualTo(SKColors.Black));
            Assert.That(frame.GetPixel(75, 75), Is.EqualTo(SKColors.Black));
        });
    }
}
