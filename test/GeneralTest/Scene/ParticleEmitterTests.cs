using System.Text.Json;
using Avalonia;
using Avalonia.Media;
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
    public void JsonParsesPerParticleFlipbook()
    {
        var definition = ParticleEmitterDefinition.FromJson("""
        {
          "particleTexture": "explosion-sheet",
          "flipbook": {
            "columns": 4,
            "rows": 2,
            "frameCount": 7,
            "framesPerSecond": 12,
            "cyclesOverLifetime": 2,
            "loop": true,
            "randomStartFrame": true
          }
        }
        """);

        Assert.That(definition.Flipbook, Is.EqualTo(new ParticleFlipbookDefinition(4, 2, 7, 12, 2, true, true)));
    }

    [Test]
    public void FlipbookCalculatesFixedRateAndLifetimeFrames()
    {
        var fixedRate = new ParticleFlipbookDefinition(4, 1, 4, FramesPerSecond: 2, Loop: true);
        var lifetime = new ParticleFlipbookDefinition(4, 1, 4, CyclesOverLifetime: 2, Loop: true);
        var clamped = fixedRate with { Loop = false };

        Assert.Multiple(() =>
        {
            Assert.That(fixedRate.GetFrameIndex(.6f, 10), Is.EqualTo(1));
            Assert.That(fixedRate.GetFrameIndex(2.1f, 10), Is.Zero);
            Assert.That(lifetime.GetFrameIndex(.625f, 1), Is.EqualTo(1));
            Assert.That(clamped.GetFrameIndex(10, 10), Is.EqualTo(3));
            Assert.That(fixedRate.GetFrameIndex(.6f, 10, startFrame: 3), Is.Zero);
        });
    }

    [Test]
    public void DefinitionRoundTripsThroughSnapshotJson()
    {
        var source = new ParticleEmitterDefinition(
            "spark",
            EmissionRate: 0,
            Shape: new ParticleShapeDefinition(ParticleShapeKind.Circle, 50, 60, Radius: 32),
            Flipbook: new ParticleFlipbookDefinition(4, 2, 7, CyclesOverLifetime: 1.5f, RandomStartFrame: true));

        var restored = JsonSerializer.Deserialize<ParticleEmitterDefinition>(JsonSerializer.Serialize(source));

        Assert.Multiple(() =>
        {
            Assert.That(restored!.ParticleTexture, Is.EqualTo(source.ParticleTexture));
            Assert.That(restored.EmissionRate, Is.Zero);
            Assert.That(restored.Shape, Is.EqualTo(source.Shape));
            Assert.That(restored.Flipbook, Is.EqualTo(source.Flipbook));
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

    [Test]
    public void FixedRateFlipbookSelectsPerParticleAtlasFrame()
    {
        var bitmap = new SKBitmap(20, 10);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { Color = SKColors.Red };
            canvas.DrawRect(0, 0, 10, 10, paint);
            paint.Color = SKColors.Blue;
            canvas.DrawRect(10, 0, 10, 10, paint);
        }
        using var texture = new SceneTexture(new DrawingImage(), bitmap);
        using var emitter = new RenderParticleEmitter("flipbook", texture, new ParticleEmitterDefinition(
            "sheet",
            MaxParticles: 1,
            InitialVelocityX: 0,
            InitialVelocityY: 0,
            Noise: 0,
            ParticleLifetime: 10,
            Shape: new ParticleShapeDefinition(ParticleShapeKind.Point, 50, 50),
            Flipbook: new ParticleFlipbookDefinition(2, 1, 2, FramesPerSecond: 2)), 0, initialBurstCount: 1);
        emitter.Update(new SceneFrameContext(TimeSpan.FromSeconds(.6), TimeSpan.FromSeconds(.6), new SKSize(100, 100)));

        using var frame = SceneRenderPipeline.Render(SceneRenderPlan.Empty, [], [emitter], new Size(100, 100));
        var pixel = frame.GetPixel(50, 50);

        Assert.That(pixel.Blue, Is.GreaterThan(pixel.Red));
    }

    [Test]
    public void RandomStartFrameIsDeterministicForAnEmitterSeed()
    {
        var bitmap = new SKBitmap(40, 10);
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint())
        {
            var colors = new[] { SKColors.Red, SKColors.Green, SKColors.Blue, SKColors.Yellow };
            for (var index = 0; index < colors.Length; index++)
            {
                paint.Color = colors[index];
                canvas.DrawRect(index * 10, 0, 10, 10, paint);
            }
        }
        using var texture = new SceneTexture(new DrawingImage(), bitmap);

        SKColor RenderSeed(int seed)
        {
            using var emitter = new RenderParticleEmitter("random-frame", texture, new ParticleEmitterDefinition(
                "sheet",
                MaxParticles: 1,
                InitialVelocityX: 0,
                InitialVelocityY: 0,
                Noise: 0,
                ParticleLifetime: 10,
                Seed: seed,
                Shape: new ParticleShapeDefinition(ParticleShapeKind.Point, 50, 50),
                Flipbook: new ParticleFlipbookDefinition(4, 1, 4, CyclesOverLifetime: 0, RandomStartFrame: true)), 0, initialBurstCount: 1);
            emitter.Update(new SceneFrameContext(TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1), new SKSize(100, 100)));
            using var frame = SceneRenderPipeline.Render(SceneRenderPlan.Empty, [], [emitter], new Size(100, 100));
            return frame.GetPixel(50, 50);
        }

        var first = RenderSeed(1);
        var replay = RenderSeed(1);
        var distinctColors = Enumerable.Range(1, 12)
            .Select(RenderSeed)
            .Select(color => (color.Red, color.Green, color.Blue))
            .Distinct()
            .Count();

        Assert.Multiple(() =>
        {
            Assert.That(replay, Is.EqualTo(first));
            Assert.That(distinctColors, Is.GreaterThan(1));
        });
    }
}
