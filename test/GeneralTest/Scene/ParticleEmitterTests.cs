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
    public void JsonParsesInitialMotionLifetimeAndFlipbookRanges()
    {
        var definition = ParticleEmitterDefinition.FromJson("""
        {
          "particleTexture": "spark",
          "rate": 72,
          "maxParticles": 240,
          "seed": 7,
          "initial": {
            "lifetime": { "min": 1, "max": 2 },
            "velocityX": { "min": -20, "max": 20 },
            "velocityY": { "min": -100, "max": -80 },
            "size": { "min": 0.5, "max": 1 },
            "rotationDegrees": { "min": 0, "max": 360 },
            "angularVelocityDegrees": { "min": -90, "max": 90 },
            "color": { "min": "#FF8040FF", "max": "#FFFFFFFF" }
          },
          "motion": {
            "gravityY": 120,
            "noise": 4,
            "drag": 0.5,
            "radialVelocity": 20,
            "orbitDegreesPerSecond": 30,
            "attractor": { "x": 320, "y": 180, "strength": 50 }
          },
          "lifetime": {
            "size": [ { "time": 0, "value": 0 }, { "time": 1, "value": 1 } ],
            "opacity": [ { "time": 0, "value": 0 }, { "time": 0.2, "value": 1 }, { "time": 1, "value": 0 } ],
            "velocity": [ { "time": 0, "value": 1 }, { "time": 1, "value": 0.2 } ],
            "rotationDegrees": [ { "time": 0, "value": 0 }, { "time": 1, "value": 180 } ],
            "color": [ { "time": 0, "color": "#FFFFFFFF" }, { "time": 1, "color": "#FF0000FF" } ]
          },
          "shape": { "type": "circle", "x": 100, "y": 120, "radius": 16 },
          "flipbook": {
            "columns": 4, "rows": 2, "frameCount": 7,
            "framesPerSecond": { "min": 10, "max": 14 },
            "startFrame": { "min": 1, "max": 3 },
            "loop": true
          }
        }
        """);

        Assert.Multiple(() =>
        {
            Assert.That(definition.InitialModule.Lifetime, Is.EqualTo(new ParticleFloatRange(1, 2)));
            Assert.That(definition.InitialModule.Color, Is.EqualTo(new ParticleColorRange("#FF8040FF", "#FFFFFFFF")));
            Assert.That(definition.MotionModule.Drag, Is.EqualTo(.5f));
            Assert.That(definition.MotionModule.Attractor, Is.EqualTo(new ParticleAttractorDefinition(320, 180, 50)));
            Assert.That(definition.LifetimeModule.Opacity, Has.Count.EqualTo(3));
            Assert.That(definition.Flipbook?.FramesPerSecond, Is.EqualTo(new ParticleFloatRange(10, 14)));
            Assert.That(definition.Flipbook?.StartFrameRange, Is.EqualTo(new ParticleIntRange(1, 3)));
            Assert.That(definition.Shape, Is.EqualTo(new ParticleShapeDefinition(ParticleShapeKind.Circle, 100, 120, Radius: 16)));
        });
    }

    [Test]
    public void InvalidRangesAreRejected()
    {
        Assert.That(
            () => ParticleEmitterDefinition.FromJson("""{ "initial": { "lifetime": { "min": 2, "max": 1 } } }"""),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("lifetime range"));
    }

    [Test]
    public void FlipbookCalculatesFixedRateAndLifetimeFrames()
    {
        var fixedRate = new ParticleFlipbookDefinition(4, 1, 4, new ParticleFloatRange(2, 2), Loop: true);
        var lifetime = new ParticleFlipbookDefinition(4, 1, 4, CyclesOverLifetime: new ParticleFloatRange(2, 2), Loop: true);
        var clamped = fixedRate with { Loop = false };

        Assert.Multiple(() =>
        {
            Assert.That(fixedRate.GetFrameIndex(.6f, 10, 2), Is.EqualTo(1));
            Assert.That(fixedRate.GetFrameIndex(2.1f, 10, 2), Is.Zero);
            Assert.That(lifetime.GetFrameIndex(.625f, 1, 2), Is.EqualTo(1));
            Assert.That(clamped.GetFrameIndex(10, 10, 2), Is.EqualTo(3));
            Assert.That(fixedRate.GetFrameIndex(.6f, 10, 2, startFrame: 3), Is.Zero);
        });
    }

    [Test]
    public void DefinitionRoundTripsThroughSnapshotJson()
    {
        var source = new ParticleEmitterDefinition(
            "spark",
            EmissionRate: 0,
            Initial: new ParticleInitialDefinition { Lifetime = new ParticleFloatRange(1, 2), Size = new ParticleFloatRange(.5f, 1) },
            Motion: new ParticleMotionDefinition { Drag = .5f, Attractor = new ParticleAttractorDefinition(50, 60, 20) },
            Lifetime: new ParticleLifetimeDefinition { Opacity = [new ParticleCurveKey(0, 0), new ParticleCurveKey(1, 1)] },
            Shape: new ParticleShapeDefinition(ParticleShapeKind.Circle, 50, 60, Radius: 32),
            Flipbook: new ParticleFlipbookDefinition(4, 2, 7, CyclesOverLifetime: new ParticleFloatRange(1, 2), StartFrame: new ParticleIntRange(0, 3)));

        var restored = JsonSerializer.Deserialize<ParticleEmitterDefinition>(JsonSerializer.Serialize(source));

        Assert.Multiple(() =>
        {
            Assert.That(restored!.Initial, Is.EqualTo(source.Initial));
            Assert.That(restored.Motion, Is.EqualTo(source.Motion));
            Assert.That(restored.LifetimeModule.Opacity, Is.EqualTo(source.LifetimeModule.Opacity));
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
        using var emitter = new RenderParticleEmitter("burst", null, Definition(maximum: 2), 0, initialBurstCount: 8);
        var size = new SKSize(100, 100);

        emitter.Update(new SceneFrameContext(TimeSpan.FromSeconds(.02), TimeSpan.FromSeconds(.02), size));
        emitter.Update(new SceneFrameContext(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(.49), size));

        Assert.That(emitter.ActiveParticleCount, Is.EqualTo(2));
    }

    [Test]
    public void PointShapeRendersBurstAtAuthoredPosition()
    {
        using var emitter = new RenderParticleEmitter("point", null, Definition(shape: new ParticleShapeDefinition(ParticleShapeKind.Point, 20, 30)), 0, initialBurstCount: 1);
        emitter.Update(Frame(.001f));

        using var frame = Render(emitter);

        Assert.Multiple(() =>
        {
            Assert.That(frame.GetPixel(20, 30), Is.Not.EqualTo(SKColors.Black));
            Assert.That(frame.GetPixel(75, 75), Is.EqualTo(SKColors.Black));
        });
    }

    [Test]
    public void LifetimeCurvesInterpolateSizeOpacityAndColor()
    {
        var lifetime = new ParticleLifetimeDefinition
        {
            Size = [new ParticleCurveKey(0, 1), new ParticleCurveKey(1, 3)],
            Opacity = [new ParticleCurveKey(0, 1), new ParticleCurveKey(1, 1)],
            Color = [new ParticleColorCurveKey(0, "#FF0000FF"), new ParticleColorCurveKey(1, "#0000FFFF")]
        };
        using var emitter = new RenderParticleEmitter("curves", null, Definition(
            initial: SmallStationaryInitial() with { Lifetime = new ParticleFloatRange(2, 2), Size = new ParticleFloatRange(.2f, .2f) },
            lifetime: lifetime), 0, initialBurstCount: 1);
        emitter.Update(Frame(1));

        using var frame = Render(emitter);
        var bounds = LitBounds(frame);
        var center = frame.GetPixel(50, 50);

        Assert.Multiple(() =>
        {
            Assert.That(bounds.Width, Is.InRange(10, 13));
            Assert.That(center.Red, Is.EqualTo(center.Blue).Within(2));
            Assert.That(center.Red, Is.GreaterThan(100));
            Assert.That(center.Green, Is.LessThan(8));
        });
    }

    [Test]
    public void VelocityCurveDragAttractorAndOrbitAffectMotion()
    {
        var initial = new ParticleInitialDefinition
        {
            Lifetime = new ParticleFloatRange(2, 2),
            VelocityX = new ParticleFloatRange(20, 20),
            VelocityY = new ParticleFloatRange(0, 0),
            Size = new ParticleFloatRange(.1f, .1f)
        };
        var motion = new ParticleMotionDefinition
        {
            Noise = 0,
            Drag = MathF.Log(2),
            OrbitDegreesPerSecond = 90,
            Attractor = new ParticleAttractorDefinition(100, 50, 10)
        };
        var lifetime = new ParticleLifetimeDefinition
        {
            Velocity = [new ParticleCurveKey(0, 1), new ParticleCurveKey(1, 0)],
            Opacity = [new ParticleCurveKey(0, 1), new ParticleCurveKey(1, 1)]
        };
        using var emitter = new RenderParticleEmitter("motion", null, Definition(initial: initial, motion: motion, lifetime: lifetime), 0, initialBurstCount: 1);
        emitter.Update(Frame(1));

        using var frame = Render(emitter);
        var bounds = LitBounds(frame);

        Assert.Multiple(() =>
        {
            Assert.That(bounds.CenterX, Is.EqualTo(50).Within(1));
            Assert.That(bounds.CenterY, Is.EqualTo(57.5).Within(1.5));
        });
    }

    [Test]
    public void RadialVelocityMovesParticlesAwayFromShapeCenter()
    {
        var shape = new ParticleShapeDefinition(ParticleShapeKind.Circle, 50, 50, Radius: 10);
        var baseline = RenderCenter(Definition(seed: 11, shape: shape, initial: SmallStationaryInitial()));
        var radial = RenderCenter(Definition(seed: 11, shape: shape, initial: SmallStationaryInitial(), motion: new ParticleMotionDefinition { Noise = 0, RadialVelocity = 10 }));
        var baselineRadius = Distance(baseline, (50, 50));
        var radialRadius = Distance(radial, (50, 50));

        Assert.That(radialRadius - baselineRadius, Is.EqualTo(10).Within(1.5));
    }

    [Test]
    public void InitialRotationIsAppliedToAtlasSprite()
    {
        using var bitmap = new SKBitmap(20, 4);
        using (var canvas = new SKCanvas(bitmap)) { canvas.Clear(SKColors.White); }
        using var texture = new SceneTexture(new DrawingImage(), bitmap);
        var initial = SmallStationaryInitial() with
        {
            Lifetime = new ParticleFloatRange(2, 2),
            Size = new ParticleFloatRange(1, 1),
            RotationDegrees = new ParticleFloatRange(30, 30),
            AngularVelocityDegrees = new ParticleFloatRange(30, 30)
        };
        var lifetime = new ParticleLifetimeDefinition
        {
            RotationDegrees = [new ParticleCurveKey(0, 0), new ParticleCurveKey(1, 60)],
            Opacity = [new ParticleCurveKey(0, 1), new ParticleCurveKey(1, 1)]
        };
        using var emitter = new RenderParticleEmitter("rotation", texture, Definition(initial: initial, lifetime: lifetime), 0, initialBurstCount: 1);
        emitter.Update(Frame(1));

        using var frame = Render(emitter);

        Assert.Multiple(() =>
        {
            Assert.That(frame.GetPixel(50, 40), Is.Not.EqualTo(SKColors.Black));
            Assert.That(frame.GetPixel(60, 50), Is.EqualTo(SKColors.Black));
        });
    }

    [Test]
    public void InitialRangesProduceSeedDeterministicParticles()
    {
        (float X, float Width, byte Red, byte Blue) RenderSeed(int seed)
        {
            var initial = SmallStationaryInitial() with
            {
                Lifetime = new ParticleFloatRange(2, 4),
                VelocityX = new ParticleFloatRange(-10, 10),
                Size = new ParticleFloatRange(.1f, .4f),
                Color = new ParticleColorRange("#FF0000FF", "#0000FFFF")
            };
            using var emitter = new RenderParticleEmitter("ranges", null, Definition(seed: seed, initial: initial), 0, initialBurstCount: 1);
            emitter.Update(Frame(.5f));
            using var frame = Render(emitter);
            var bounds = LitBounds(frame);
            var center = frame.GetPixel((int)MathF.Round(bounds.CenterX), (int)MathF.Round(bounds.CenterY));
            return (bounds.CenterX, bounds.Width, center.Red, center.Blue);
        }

        var first = RenderSeed(3);
        var replay = RenderSeed(3);
        var distinct = Enumerable.Range(1, 8).Select(RenderSeed).Distinct().Count();

        Assert.Multiple(() =>
        {
            Assert.That(replay, Is.EqualTo(first));
            Assert.That(distinct, Is.GreaterThan(1));
        });
    }

    [Test]
    public void FixedRateFlipbookSelectsPerParticleAtlasFrame()
    {
        using var bitmap = new SKBitmap(20, 10);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { Color = SKColors.Red };
            canvas.DrawRect(0, 0, 10, 10, paint);
            paint.Color = SKColors.Blue;
            canvas.DrawRect(10, 0, 10, 10, paint);
        }
        using var texture = new SceneTexture(new DrawingImage(), bitmap);
        var definition = Definition(initial: SmallStationaryInitial(), flipbook: new ParticleFlipbookDefinition(
            2, 1, 2, FramesPerSecond: new ParticleFloatRange(2, 2)));
        using var emitter = new RenderParticleEmitter("flipbook", texture, definition, 0, initialBurstCount: 1);
        emitter.Update(Frame(.6f));

        using var frame = Render(emitter);
        var pixel = frame.GetPixel(50, 50);

        Assert.That(pixel.Blue, Is.GreaterThan(pixel.Red));
    }

    [Test]
    public void RangedStartFrameIsDeterministicForAnEmitterSeed()
    {
        using var bitmap = new SKBitmap(40, 10);
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
            var definition = Definition(seed: seed, initial: SmallStationaryInitial(), flipbook: new ParticleFlipbookDefinition(
                4, 1, 4, CyclesOverLifetime: new ParticleFloatRange(0, 0), StartFrame: new ParticleIntRange(0, 3)));
            using var emitter = new RenderParticleEmitter("random-frame", texture, definition, 0, initialBurstCount: 1);
            emitter.Update(Frame(.001f));
            using var frame = Render(emitter);
            return frame.GetPixel(50, 50);
        }

        var first = RenderSeed(1);
        var replay = RenderSeed(1);
        var distinctColors = Enumerable.Range(1, 12).Select(RenderSeed).Select(color => (color.Red, color.Green, color.Blue)).Distinct().Count();

        Assert.Multiple(() =>
        {
            Assert.That(replay, Is.EqualTo(first));
            Assert.That(distinctColors, Is.GreaterThan(1));
        });
    }

    private static ParticleEmitterDefinition Definition(
        int maximum = 8,
        int seed = 7,
        ParticleInitialDefinition? initial = null,
        ParticleMotionDefinition? motion = null,
        ParticleLifetimeDefinition? lifetime = null,
        ParticleShapeDefinition? shape = null,
        ParticleFlipbookDefinition? flipbook = null) => new(
            "", MaxParticles: maximum, Seed: seed,
            Initial: initial ?? SmallStationaryInitial(),
            Motion: motion ?? new ParticleMotionDefinition { Noise = 0 },
            Lifetime: lifetime,
            Shape: shape ?? new ParticleShapeDefinition(ParticleShapeKind.Point, 50, 50),
            Flipbook: flipbook);

    private static ParticleInitialDefinition SmallStationaryInitial() => new()
    {
        Lifetime = new ParticleFloatRange(10, 10),
        VelocityX = new ParticleFloatRange(0, 0),
        VelocityY = new ParticleFloatRange(0, 0),
        Size = new ParticleFloatRange(.2f, .2f)
    };

    private static SceneFrameContext Frame(float seconds) => new(TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(seconds), new SKSize(100, 100));
    private static SKBitmap Render(RenderParticleEmitter emitter) => SceneRenderPipeline.Render(SceneRenderPlan.Empty, [], [emitter], new Size(100, 100));
    private static (float X, float Y) RenderCenter(ParticleEmitterDefinition definition)
    {
        using var emitter = new RenderParticleEmitter("center", null, definition, 0, initialBurstCount: 1);
        emitter.Update(Frame(1));
        using var frame = Render(emitter);
        var bounds = LitBounds(frame);
        return (bounds.CenterX, bounds.CenterY);
    }

    private static (int Left, int Top, int Right, int Bottom, float Width, float CenterX, float CenterY) LitBounds(SKBitmap bitmap)
    {
        var left = bitmap.Width; var top = bitmap.Height; var right = -1; var bottom = -1;
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            var color = bitmap.GetPixel(x, y);
            if (color.Red == 0 && color.Green == 0 && color.Blue == 0) continue;
            left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y);
        }
        Assert.That(right, Is.GreaterThanOrEqualTo(left));
        return (left, top, right, bottom, right - left + 1, (left + right) / 2f, (top + bottom) / 2f);
    }

    private static float Distance((float X, float Y) point, (float X, float Y) center) => MathF.Sqrt(MathF.Pow(point.X - center.X, 2) + MathF.Pow(point.Y - center.Y, 2));
}
