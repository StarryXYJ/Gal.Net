using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.Core.Primitives;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Core.Variable;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;
using GalNet.Primitives.Builtins;
using GalNet.Runtime.Runtime;

namespace GeneralTest.Runtime;

public class BuiltinPrimitiveInstanceTests
{
    [TestCase("1 +")]
    [TestCase("[unknown]")]
    public void FailedVariableExpressionPreservesExistingValue(string expression)
    {
        using var view = new GameRegistration().AddBuiltins().BuildView();
        var runtime = new GameRuntime(null);
        runtime.SetVariable("score", 42);
        var instance = view.Dispatch(Primitive(BuiltinEntryIds.SetVariable,
            new Dictionary<string, object?> { ["target"] = "score", ["expression"] = expression }), runtime, default);
        Assert.That(instance.IsCompleted, Is.True);
        Assert.That(runtime.GetVariable("score")!.AsInt(), Is.EqualTo(42));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AnimationStateVersionTwoRoundTripsAnimateAndTimelineAndRejectsVersionOne(bool timeline)
    {
        using var view = new GameRegistration().AddBuiltins().BuildView();
        var runtime = new GameRuntime(null, stateCatalog: view.StateCatalog);
        runtime.State.Table<LoopAnimationState>().Add(new()
        {
            Id = "loop",
            Request = timeline ? null : new AnimationRequest { PlaybackHandleId = "loop", LoopMode = AnimationLoopMode.Loop },
            Timeline = timeline ? new TimelineDefinition { PlaybackHandleId = "loop", DurationFrames = 30, LoopMode = AnimationLoopMode.Loop } : null
        });
        var sections = runtime.State.Capture();
        var section = sections["builtin.animations"];
        Assert.That(section.Version, Is.EqualTo(2));
        var restored = GalNet.Core.Runtime.GameState.Restore(view.StateCatalog, sections).Table<LoopAnimationState>().Values.Single();
        Assert.That(restored.Id, Is.EqualTo("loop"));
        Assert.That(restored.Timeline is not null, Is.EqualTo(timeline));
        Assert.That(restored.Request is not null, Is.EqualTo(!timeline));
        Assert.That(section.Payload[0].TryGetProperty("timeline", out _), Is.True);
        Assert.That(section.Payload[0].TryGetProperty("plan", out _), Is.False);
        if (timeline) Assert.That(restored.Timeline!.DurationFrames, Is.EqualTo(30));
        sections["builtin.animations"] = new(1, section.Payload);
        Assert.Throws<InvalidDataException>(() => GalNet.Core.Runtime.GameState.Restore(view.StateCatalog, sections));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TypedLayerStillAcceptsObjectAndEmbeddedJsonTransform(bool embeddedJson)
    {
        var presenter = new RecordingLayerPresenter();
        using var view = new GameRegistration().AddBuiltins(layerPresenter: presenter).BuildView();
        var values = new Dictionary<string, object?>
        {
            ["handleId"] = "hero",
            ["assetId"] = "portrait",
            ["transform"] = embeddedJson ? (object)"{\"x\":12}" : new { x = 12 },
            ["displayMode"] = null
        };
        var runtime = BuiltinTestRuntime.Create(null);
        var instance = view.Dispatch(Primitive(BuiltinEntryIds.ShowLayer, values), runtime, default);
        Assert.That(instance.IsCompleted, Is.True);
        Assert.That(presenter.Shown.Single().Transform.X, Is.EqualTo(12));
        Assert.That(presenter.Shown.Single().DisplayMode, Is.EqualTo(LayerDisplayMode.Native));
        Assert.That(runtime.State.Table<Layer>().Values.Single().Transform.X, Is.EqualTo(12));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task TypedAnimationPreservesOmittedStartAndCompiledBatchEnvelope(bool provideStart)
    {
        var catalog = new TargetProfileEntryCatalog([BuiltinSchemas.CreateAnimationsModule()]);
        var values = new Dictionary<string, JsonElement>
        {
            ["handleId"] = JsonSerializer.SerializeToElement("hero"),
            ["property"] = JsonSerializer.SerializeToElement("opacity"),
            ["to"] = JsonSerializer.SerializeToElement(.5f),
            ["batchId"] = JsonSerializer.SerializeToElement("typed-batch")
        };
        if (provideStart) values["from"] = JsonSerializer.SerializeToElement(0f);
        var compiled = GalNet.Core.Compilation.GalgroupCompiler.Compile(new GalNet.Core.Serialization.GroupDocument
        { Entries = [new() { Id = "animate", Type = BuiltinEntryIds.Animate, Parameters = values }] }, catalog).Document.Entries.Single();
        Assert.That(compiled.Arguments.TryGetProperty("batchId", out _), Is.False);
        var presenter = new RecordingAnimationPresenter();
        using var view = new GameRegistration().UseCatalog(catalog).AddBuiltinImplementations(catalog, animationPresenter: presenter).BuildView();
        using var cancellation = new CancellationTokenSource();
        var instance = view.Dispatch(new PrimitiveEntry(compiled.TypeId, compiled.Arguments, compiled.BatchId), BuiltinTestRuntime.Create(null), cancellation.Token);
        await WaitUntilAsync(() => presenter.Animations.Count == 1);
        Assert.That(instance.BatchId, Is.EqualTo("typed-batch"));
        Assert.That(presenter.Animations.Single().From, Is.EqualTo(provideStart ? 0f : (float?)null));
        cancellation.Cancel();
        await instance.PresentationCompletion;
    }

    [TestCase(PrimitiveInvocationMode.Quick)]
    [TestCase(PrimitiveInvocationMode.CatchUp)]
    public void AdditiveAnimationDoesNotApplyTerminalDeltaTwice(PrimitiveInvocationMode mode)
    {
        var presenter = new RecordingAnimationPresenter();
        using var view = new GameRegistration().AddBuiltins(animationPresenter: presenter).BuildView();
        var runtime = BuiltinTestRuntime.Create(null);
        var layer = runtime.Scene().GetOrAdd("hero", id => new Layer { Id = id, Opacity = .2f });
        var instance = view.Dispatch(Primitive(BuiltinEntryIds.Animate, new
        {
            playbackHandleId = "fade",
            handleId = "hero",
            property = "opacity",
            to = .3f,
            duration = 3,
            curve = "Linear",
            blocking = "true",
            skippable = "true",
            loopMode = "Once",
            blendMode = "Additive"
        }), runtime, CancellationToken.None)!;
        var terminal = layer.Opacity;
        instance.ApplyPlaybackMode(mode);
        Assert.That(instance.IsCompleted, Is.True);
        Assert.That(layer.Opacity, Is.EqualTo(terminal));
    }
    private static readonly string[] FadeCompleted = ["fade"];
    private static readonly string[] BlurStopped = ["blur-1"];
    private static readonly string[] SnowStopped = ["snow"];
    private static readonly int[] TimedBurstCounts = [8, 12];
    private static readonly string[] TimedSnowStopped = ["timed-snow"];
    private static readonly string[] NewLayerIds = ["new"];

    private static readonly JsonSerializerOptions AnimationJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Test]
    public void AnimationAnimateCommitsFinalStateAndCanBeSkippedThroughPresenter()
    {
        var presenter = new RecordingAnimationPresenter();
        using var view = new GameRegistration().AddBuiltins(animationPresenter: presenter).BuildView();
        var runtime = BuiltinTestRuntime.Create(null);
        var layer = runtime.Scene().GetOrAdd("hero", id => new Layer { Id = id, Opacity = 0 });

        var instance = view.Dispatch(Primitive(BuiltinEntryIds.Animate, new
        {
            playbackHandleId = "fade",
            handleId = "hero",
            property = "opacity",
            to = 1,
            duration = 3,
            curve = "Linear",
            blocking = "true",
            skippable = "true",
            loopMode = "Once",
            blendMode = "Replace"
        }, "scene"), runtime, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(layer.Opacity, Is.EqualTo(1));
            Assert.That(instance, Is.TypeOf<AnimationPrimitiveInstance>());
            Assert.That(instance!.IsBlocking, Is.True);
            Assert.That(instance.IsSkippable, Is.True);
            Assert.That(instance.BatchId, Is.EqualTo("scene"));
            Assert.That(instance.IsCompleted, Is.False);
        });

        instance!.Skip();

        Assert.Multiple(() =>
        {
            Assert.That(presenter.CompletedImmediately, Is.EqualTo(FadeCompleted));
            Assert.That(instance.IsCompleted, Is.True);
        });
    }

    [Test]
    public async Task EffectApplyAndStopUpdateRuntimeStateAndPresenter()
    {
        var presenter = new RecordingEffectPresenter();
        using var view = new GameRegistration().AddBuiltins(effectPresenter: presenter).BuildView();
        var runtime = BuiltinTestRuntime.Create(null);
        var layer = runtime.Scene().GetOrAdd("hero", id => new Layer { Id = id });

        var apply = view.Dispatch(Primitive(BuiltinEntryIds.ApplyEffect, new
        {
            id = "test.blur",
            program = "",
            instanceId = "blur-1",
            targetHandleId = "hero",
            order = 2,
            parameters = new { radius = 4 }
        }), runtime, CancellationToken.None);

        await WaitUntilAsync(() => apply!.IsCompleted);

        Assert.Multiple(() =>
        {
            Assert.That(runtime.State.Table<EffectInstance>().Values.Single().Parameters, Does.Contain("radius"));
            Assert.That(presenter.Started.Single().InstanceId, Is.EqualTo("blur-1"));
        });

        var stop = view.Dispatch(Primitive(BuiltinEntryIds.StopEffect, new { instanceId = "blur-1" }), runtime, CancellationToken.None);

        await WaitUntilAsync(() => stop!.IsCompleted);

        Assert.Multiple(() =>
        {
            Assert.That(runtime.State.Table<EffectInstance>().Values, Is.Empty);
            Assert.That(presenter.Stopped, Is.EqualTo(BlurStopped));
        });
    }

    [Test]
    public async Task ParticlePlayAndStopUpdateRuntimeStateAndPresenter()
    {
        var presenter = new RecordingParticlePresenter();
        using var view = new GameRegistration().AddBuiltins(particlePresenter: presenter).BuildView();
        var runtime = BuiltinTestRuntime.Create(null);

        var play = view.Dispatch(Primitive(BuiltinEntryIds.PlayParticleEmitter, new
        {
            instanceId = "snow",
            z = 42,
            parameters = new
            {
                particleTexture = "snowflake",
                rate = 72,
                maxParticles = 240,
                seed = 20260929,
                initial = new
                {
                    lifetime = new { min = 3, max = 4 },
                    velocityX = new { min = -28, max = -16 },
                    velocityY = new { min = 68, max = 92 },
                    size = new { min = 0.5, max = 1.0 }
                },
                motion = new { noise = 24, gravityY = 12 },
                flipbook = new { columns = 4, rows = 2, frameCount = 7, framesPerSecond = new { min = 10, max = 14 }, loop = true }
            }
        }), runtime, CancellationToken.None);

        await WaitUntilAsync(() => play!.IsCompleted);

        Assert.Multiple(() =>
        {
            Assert.That(runtime.State.Table<ParticleEmitterInstance>().Values, Has.Count.EqualTo(1));
            Assert.That(runtime.State.Table<ParticleEmitterInstance>().Values[0].Definition.ParticleTexture, Is.EqualTo("snowflake"));
            Assert.That(runtime.State.Table<ParticleEmitterInstance>().Values[0].Definition.MotionModule.GravityY, Is.EqualTo(12));
            Assert.That(runtime.State.Table<ParticleEmitterInstance>().Values[0].Definition.InitialModule.VelocityX, Is.EqualTo(new ParticleFloatRange(-28, -16)));
            Assert.That(runtime.State.Table<ParticleEmitterInstance>().Values[0].Definition.Flipbook?.FramesPerSecond, Is.EqualTo(new ParticleFloatRange(10, 14)));
            Assert.That(runtime.Scene().TryGet<ParticleEmitterInstance>("snow", out _), Is.True);
            Assert.That(presenter.Started.Single().InstanceId, Is.EqualTo("snow"));
            Assert.That(presenter.Started.Single().Z, Is.EqualTo(42));
        });

        var animate = view.Dispatch(Primitive(BuiltinEntryIds.Animate, new
        {
            playbackHandleId = "snow-rate",
            handleId = "snow",
            property = "emissionRate",
            to = 18,
            duration = 0.5,
            curve = "Linear",
            blocking = "false",
            skippable = "false",
            loopMode = "Once",
            blendMode = "Replace"
        }), runtime, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(animate!.IsCompleted, Is.True);
            Assert.That(runtime.State.Table<ParticleEmitterInstance>().Values[0].AnimationValues["emissionRate"], Is.EqualTo(18));
        });

        var stop = view.Dispatch(
            Primitive(BuiltinEntryIds.StopParticleEmitter, new { instanceId = "snow" }),
            runtime,
            CancellationToken.None);

        await WaitUntilAsync(() => stop!.IsCompleted);

        Assert.Multiple(() =>
        {
            Assert.That(runtime.State.Table<ParticleEmitterInstance>().Values, Is.Empty);
            Assert.That(runtime.Scene().TryGet<ParticleEmitterInstance>("snow", out _), Is.False);
            Assert.That(presenter.Stopped, Is.EqualTo(SnowStopped));
        });
    }

    [Test]
    public async Task ParticleBurstIsFireAndForgetAndDoesNotCreateRuntimeState()
    {
        var presenter = new RecordingParticlePresenter();
        using var view = new GameRegistration().AddBuiltins(particlePresenter: presenter).BuildView();
        var runtime = BuiltinTestRuntime.Create(null);

        var burst = view.Dispatch(Primitive(BuiltinEntryIds.BurstParticles, new
        {
            count = 24,
            z = 110,
            parameters = new
            {
                particleTexture = "spark",
                maxParticles = 24,
                seed = 7,
                flipbook = new { columns = 2, rows = 2, frameCount = 4, cyclesOverLifetime = new { min = 1, max = 1 } },
                shape = new { type = "circle", x = 320, y = 180, radius = 16 }
            }
        }), runtime, CancellationToken.None);

        await WaitUntilAsync(() => burst!.IsCompleted);

        Assert.Multiple(() =>
        {
            Assert.That(runtime.State.Table<ParticleEmitterInstance>().Values, Is.Empty);
            Assert.That(presenter.Bursts.Single().Count, Is.EqualTo(24));
            Assert.That(presenter.Bursts.Single().Z, Is.EqualTo(110));
            Assert.That(presenter.Bursts.Single().Definition.EmissionRate, Is.Zero);
            Assert.That(presenter.Bursts.Single().Definition.Shape?.Type, Is.EqualTo(ParticleShapeKind.Circle));
            Assert.That(presenter.Bursts.Single().Definition.Flipbook?.CyclesOverLifetime, Is.EqualTo(new ParticleFloatRange(1, 1)));
        });
    }

    [Test]
    public async Task TimelineCanScheduleParticleLifetimeAndMultipleBursts()
    {
        var particles = new RecordingParticlePresenter();
        var animations = new RecordingAnimationPresenter();
        using var view = new GameRegistration().AddBuiltins(
            animationPresenter: animations,
            particlePresenter: particles).BuildView();
        var runtime = BuiltinTestRuntime.Create(null);
        var burstParameters = new
        {
            particleTexture = "spark",
            maxParticles = 12,
            shape = new { type = "point", x = 100, y = 120 }
        };
        var timeline = new TimelineDefinition
        {
            PlaybackHandleId = "burst-sequence",
            FrameRate = 100,
            DurationFrames = 1,
            Blocking = true,
            Events =
            {
                new TimelineEventDefinition
                {
                    Frame = 0,
                    Type = BuiltinEntryIds.PlayParticleEmitter,
                    Parameters = Parameters(
                        ("instanceId", "timed-snow"),
                        ("z", 80),
                        ("parameters", new { particleTexture = "snow", rate = 20, shape = new { type = "box", x = 320, y = 0, width = 640 } }))
                },
                new TimelineEventDefinition { Frame = 0, Type = BuiltinEntryIds.BurstParticles, Parameters = Parameters(("count", 8), ("z", 90), ("parameters", burstParameters)) },
                new TimelineEventDefinition { Frame = 1, Type = BuiltinEntryIds.BurstParticles, Parameters = Parameters(("count", 12), ("z", 95), ("parameters", burstParameters)) },
                new TimelineEventDefinition { Frame = 1, Type = BuiltinEntryIds.StopParticleEmitter, Parameters = Parameters(("instanceId", "timed-snow")) }
            }
        };

        var instance = view.Dispatch(Primitive(BuiltinEntryIds.PlayTimeline, new { timeline }), runtime, CancellationToken.None);
        await WaitUntilAsync(() => instance!.IsCompleted);

        Assert.Multiple(() =>
        {
            Assert.That(particles.Started.Single().InstanceId, Is.EqualTo("timed-snow"));
            Assert.That(particles.Bursts.Select(request => request.Count), Is.EqualTo(TimedBurstCounts));
            Assert.That(particles.Stopped, Is.EqualTo(TimedSnowStopped));
            Assert.That(runtime.State.Table<ParticleEmitterInstance>().Values, Is.Empty);
        });
    }

    [Test]
    public async Task SkippingTimelineDoesNotBackfillFutureParticleBurst()
    {
        var runtime = BuiltinTestRuntime.Create(null);
        var particles = new RecordingParticlePresenter();
        var animations = new LoopingAnimationPresenter();
        using var view = new GameRegistration().AddBuiltins(
            animationPresenter: animations,
            particlePresenter: particles).BuildView();
        var timeline = new TimelineDefinition
        {
            PlaybackHandleId = "skipped-burst",
            FrameRate = 1,
            DurationFrames = 10,
            Blocking = true,
            Skippable = true,
            Events =
            {
                new TimelineEventDefinition
                {
                    Frame = 10,
                    Type = BuiltinEntryIds.BurstParticles,
                    Parameters = Parameters(
                        ("count", 8),
                        ("parameters", new { particleTexture = "spark", shape = new { type = "point", x = 0, y = 0 } }))
                }
            }
        };

        var instance = view.Dispatch(Primitive(BuiltinEntryIds.PlayTimeline, new { timeline }), runtime, CancellationToken.None)!;
        instance.Skip();
        await WaitUntilAsync(() => instance.IsCompleted);

        Assert.That(particles.Bursts, Is.Empty);
    }

    [Test]
    public async Task LoopingTimelineRetriggersParticleBurstEachCycle()
    {
        var runtime = BuiltinTestRuntime.Create(null);
        var particles = new RecordingParticlePresenter();
        var animations = new LoopingAnimationPresenter();
        using var view = new GameRegistration().AddBuiltins(
            animationPresenter: animations,
            particlePresenter: particles).BuildView();
        using var scope = new CancellationTokenSource();
        var timeline = new TimelineDefinition
        {
            PlaybackHandleId = "repeating-burst",
            FrameRate = 100,
            DurationFrames = 2,
            LoopMode = AnimationLoopMode.Loop,
            Events =
            {
                new TimelineEventDefinition
                {
                    Frame = 1,
                    Type = BuiltinEntryIds.BurstParticles,
                    Parameters = Parameters(
                        ("count", 4),
                        ("parameters", new { particleTexture = "spark", shape = new { type = "point", x = 0, y = 0 } }))
                }
            }
        };

        var instance = view.Dispatch(Primitive(BuiltinEntryIds.PlayTimeline, new { timeline }), runtime, scope.Token)!;
        await Task.Delay(50);
        Assert.That(particles.Bursts, Is.Empty, "Events must not run ahead of a stalled presentation clock.");
        await animations.AdvanceFrameAsync(1);
        Assert.That(particles.Bursts.Count, Is.EqualTo(1));
        await animations.AdvanceFrameAsync(3);
        Assert.That(particles.Bursts.Count, Is.EqualTo(2));
        scope.Cancel();
        await WaitUntilAsync(() => instance.IsCompleted);

        Assert.That(particles.Bursts.Count, Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public async Task RestoringLoopTimelineDoesNotRepeatAlreadyCommittedFrameZeroEvents()
    {
        var animations = new LoopingAnimationPresenter();
        var particles = new RecordingParticlePresenter();
        using var view = new GameRegistration().AddBuiltins(animationPresenter: animations, particlePresenter: particles).BuildView();
        var runtime = BuiltinTestRuntime.Create(null);
        runtime.State.Table<LoopAnimationState>().Add(new LoopAnimationState
        {
            Id = "loop",
            Timeline = new TimelineDefinition
            {
                PlaybackHandleId = "loop",
                DurationFrames = 10,
                LoopMode = AnimationLoopMode.Loop,
                Events = { new TimelineEventDefinition
                {
                    Frame = 0, Type = BuiltinEntryIds.BurstParticles,
                    Parameters = Parameters(("parameters", new { particleTexture = "snowflake" }), ("count", 1), ("z", 0))
                } }
            }
        });
        await using var restorer = new StateRestoreSession(view);
        await restorer.RestoreAsync(runtime);
        await animations.AdvanceFrameAsync(0);
        Assert.That(particles.Bursts, Is.Empty);
        await animations.AdvanceFrameAsync(11);
        Assert.That(particles.Bursts, Has.Count.EqualTo(1), "A subsequent loop cycle still owns its frame-zero event.");
    }
    [Test]
    public async Task PersistentPresentationReplayRestartsEffectsAndParticlesWithSavedAnimationValues()
    {
        var runtime = BuiltinTestRuntime.Create(null);
        var state = runtime.State;
        state.Table<Layer>().Add(new Layer { Id = "background", AssetId = "background" });
        state.Table<Layer>().Add(new Layer { Id = "ambient-overlay", AssetId = "overlay.png" });
        state.Table<EffectInstance>().Add(new EffectInstance
        {
            Id = "grade",
            EffectId = "color-grade",
            ProgramResource = "grade-program",
            TargetHandleId = "background",
            Order = 3,
            Parameters = "{\"brightness\":0.25}",
            AnimationValues = new Dictionary<string, float> { ["brightness"] = .5f }
        });
        state.Table<ParticleEmitterInstance>().Add(new ParticleEmitterInstance("snow",
            new ParticleEmitterDefinition("snowflake", EmissionRate: 72, MaxParticles: 240,
                Motion: new ParticleMotionDefinition { GravityY = 12 }), 42)
        { AnimationValues = new Dictionary<string, float> { ["emissionRate"] = 18, ["particleScale"] = 1.5f } });
        state.Table<LoopAnimationState>().Add(new LoopAnimationState
        {
            Id = "snow-pulse",
            Request = new AnimationRequest
            {
                PlaybackHandleId = "snow-pulse",
                HandleId = "snow",
                Property = "particleScale",
                From = 1.5f,
                To = 2,
                DurationSeconds = .75,
                CurveKind = BuiltinAnimationCurve.EaseInOut,
                LoopMode = AnimationLoopMode.PingPong
            }
        });
        var effectPresenter = new RecordingEffectPresenter();
        var particlePresenter = new RecordingParticlePresenter();
        var animationPresenter = new RecordingAnimationPresenter();
        var layerPresenter = new RecordingLayerPresenter();
        state.Table<LoopAnimationState>().Add(new LoopAnimationState
        {
            Id = "snow-timeline",
            Timeline = new TimelineDefinition
            {
                PlaybackHandleId = "snow-timeline",
                DurationFrames = 24,
                LoopMode = AnimationLoopMode.Loop,
                Tracks = { Track("snow", "emissionRate", 18, 36, 24) }
            }
        });
        using var restoreView = new GameRegistration().AddBuiltins(effectPresenter: effectPresenter, particlePresenter: particlePresenter,
            animationPresenter: animationPresenter, layerPresenter: layerPresenter).BuildView();
        await using var restoration = new StateRestoreSession(restoreView);
        await restoration.RestoreAsync(runtime);

        var effectRequest = effectPresenter.Started.Single();
        var particleRequest = particlePresenter.Started.Single();
        var animationRequest = animationPresenter.Animations.Single();
        var timeline = animationPresenter.Timelines.Single();
        Assert.Multiple(() =>
        {
            Assert.That(effectRequest.InstanceId, Is.EqualTo("grade"));
            Assert.That(effectRequest.ProgramResource, Is.EqualTo("grade-program"));
            Assert.That(effectRequest.AnimationValues["brightness"], Is.EqualTo(0.5f));
            Assert.That(particleRequest.InstanceId, Is.EqualTo("snow"));
            Assert.That(particleRequest.Definition.ParticleTexture, Is.EqualTo("snowflake"));
            Assert.That(particleRequest.Definition.MaxParticles, Is.EqualTo(240));
            Assert.That(particleRequest.Z, Is.EqualTo(42));
            Assert.That(particleRequest.AnimationValues["emissionRate"], Is.EqualTo(18));
            Assert.That(particleRequest.AnimationValues["particleScale"], Is.EqualTo(1.5f));
            Assert.That(animationRequest.PlaybackHandleId, Is.EqualTo("snow-pulse"));
            Assert.That(animationRequest.HandleId, Is.EqualTo("snow"));
            Assert.That(animationRequest.Property, Is.EqualTo("particleScale"));
            Assert.That(animationRequest.From, Is.EqualTo(1.5f));
            Assert.That(animationRequest.To, Is.EqualTo(2));
            Assert.That(animationRequest.DurationSeconds, Is.EqualTo(0.75));
            Assert.That(animationRequest.CurveKind, Is.EqualTo(BuiltinAnimationCurve.EaseInOut));
            Assert.That(animationRequest.LoopMode, Is.EqualTo(AnimationLoopMode.PingPong));
            Assert.That(timeline.PlaybackHandleId, Is.EqualTo("snow-timeline"));
            Assert.That(timeline.LoopMode, Is.EqualTo(AnimationLoopMode.Loop));
            Assert.That(timeline.Tracks.Single().Keys[0].Value, Is.EqualTo(18));
            Assert.That(layerPresenter.Shown.Single(value => value.HandleId == "ambient-overlay").AssetId, Is.EqualTo("overlay.png"));
        });
    }

    [Test]
    public async Task LoopingParticleAnimationKeepsFrameZeroValueAndCompleteReplayDefinition()
    {
        var particles = new RecordingParticlePresenter();
        var animations = new RecordingAnimationPresenter();
        using var view = new GameRegistration().AddBuiltins(
            animationPresenter: animations,
            particlePresenter: particles).BuildView();
        var runtime = BuiltinTestRuntime.Create(null);

        var play = view.Dispatch(Primitive(BuiltinEntryIds.PlayParticleEmitter, new
        {
            instanceId = "snow",
            parameters = new { particleTexture = "snowflake", rate = 72 }
        }), runtime, CancellationToken.None);
        await WaitUntilAsync(() => play!.IsCompleted);

        var loop = view.Dispatch(Primitive(BuiltinEntryIds.Animate, new
        {
            playbackHandleId = "snow-rate-loop",
            handleId = "snow",
            property = "emissionRate",
            to = 18,
            duration = 1.25,
            curve = "EaseInOut",
            blocking = "false",
            skippable = "false",
            loopMode = "PingPong",
            blendMode = "Replace"
        }), runtime, CancellationToken.None);

        var active = runtime.State.Table<LoopAnimationState>().Values.Single();
        Assert.Multiple(() =>
        {
            Assert.That(loop!.IsBlocking, Is.False);
            Assert.That(loop.IsCompleted, Is.False);
            Assert.That(runtime.State.Table<ParticleEmitterInstance>().Values.Single().AnimationValues["emissionRate"], Is.EqualTo(72));
            Assert.That(active.Request, Is.Not.Null);
            Assert.That(active.Request!.LoopMode, Is.EqualTo(AnimationLoopMode.PingPong));
            Assert.That(active.Request!.From, Is.EqualTo(72));
            Assert.That(active.Request!.To, Is.EqualTo(18));
            Assert.That(active.Request!.DurationSeconds, Is.EqualTo(1.25));
            Assert.That(active.Request!.CurveKind, Is.EqualTo(BuiltinAnimationCurve.EaseInOut));
            Assert.That(active.Request!.BlendMode, Is.EqualTo(AnimationBlendMode.Replace));
        });
    }

    [Test]
    public void LoopingTimelineCommitsOnlyFrameZeroStateAndSavesCompleteTimeline()
    {
        using var view = new GameRegistration().AddBuiltins().BuildView();
        var runtime = BuiltinTestRuntime.Create(null);
        runtime.Scene().GetOrAdd("old", id => new Layer { Id = id, AssetId = "old.png", Opacity = 1 });
        var timeline = new TimelineDefinition
        {
            PlaybackHandleId = "ambient-loop",
            DurationFrames = 30,
            LoopMode = AnimationLoopMode.Loop,
            Tracks = { Track("old", "opacity", 0.25f, 0.75f, 30) },
            Events =
            {
                new TimelineEventDefinition
                {
                    Frame = 30,
                    Type = BuiltinEntryIds.HideLayer,
                    Parameters = Parameters(("handleId", "old"))
                }
            }
        };

        view.Dispatch(Primitive(BuiltinEntryIds.PlayTimeline, new { timeline }), runtime, CancellationToken.None);

        var active = runtime.State.Table<LoopAnimationState>().Values.Single();
        var replayedTimeline = active.Timeline!;
        Assert.Multiple(() =>
        {
            Assert.That(runtime.Scene().TryGet<Layer>("old", out var old), Is.True);
            Assert.That(old.Opacity, Is.EqualTo(0.25f));
            Assert.That(active.Timeline!.LoopMode, Is.EqualTo(AnimationLoopMode.Loop));
            Assert.That(replayedTimeline.PlaybackHandleId, Is.EqualTo("ambient-loop"));
            Assert.That(replayedTimeline.Tracks.Single().Keys.Last().Value, Is.EqualTo(0.75f));
            Assert.That(replayedTimeline.Events.Single().Type, Is.EqualTo(BuiltinEntryIds.HideLayer));
        });
    }

    [Test]
    public async Task TimelineCommitsLayerEventsAndFinalTrackValues()
    {
        var animation = new RecordingAnimationPresenter();
        var layers = new RecordingLayerPresenter();
        using var view = new GameRegistration().AddBuiltins(
            layerPresenter: layers,
            animationPresenter: animation).BuildView();
        var runtime = BuiltinTestRuntime.Create(null);
        var oldLayer = runtime.Scene().GetOrAdd("old", id => new Layer { Id = id, AssetId = "old.png", Opacity = 1 });
        var timeline = new TimelineDefinition
        {
            PlaybackHandleId = "crossfade",
            FrameRate = 60,
            DurationFrames = 12,
            Blocking = true,
            Skippable = true,
            Tracks =
            {
                Track("old", "opacity", 1, 0, 12),
                Track("new", "opacity", 0, 1, 12)
            },
            Events =
            {
                new TimelineEventDefinition
                {
                    Frame = 0,
                    Type = BuiltinEntryIds.ShowLayer,
                    Parameters = Parameters(
                        ("handleId", "new"),
                        ("assetId", "new.png"),
                        ("transform", new { }),
                        ("z", 4),
                        ("opacity", 0),
                        ("displayMode", "Native"))
                },
                new TimelineEventDefinition
                {
                    Frame = 12,
                    Type = BuiltinEntryIds.HideLayer,
                    Parameters = Parameters(("handleId", "old"))
                }
            }
        };

        var instance = view.Dispatch(Primitive(BuiltinEntryIds.PlayTimeline, new { timeline }), runtime, CancellationToken.None);
        await WaitUntilAsync(() => animation.Timelines.Count == 1);

        Assert.Multiple(() =>
        {
            Assert.That(runtime.Scene().TryGet<Layer>("old", out _), Is.False);
            Assert.That(runtime.Scene().TryGet<Layer>("new", out var newLayer), Is.True);
            Assert.That(newLayer.Opacity, Is.EqualTo(1));
            Assert.That(runtime.State.Table<Layer>().Values.Select(layer => layer.Id), Is.EqualTo(NewLayerIds));
            Assert.That(instance, Is.TypeOf<TimelinePrimitiveInstance>());
            Assert.That(animation.Timelines.Single().PlaybackHandleId, Is.EqualTo("crossfade"));
            Assert.That(oldLayer.Opacity, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task FlowWaitBlocksUntilSkippedOrElapsed()
    {
        using var view = new GameRegistration().AddBuiltins().BuildView();
        var runtime = BuiltinTestRuntime.Create(null);

        var instance = view.Dispatch(Primitive(BuiltinEntryIds.Wait, new { duration = 10 }, "wait"), runtime, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(instance!.IsBlocking, Is.True);
            Assert.That(instance.IsSkippable, Is.True);
            Assert.That(instance.IsCompleted, Is.False);
        });

        instance!.Skip();
        await WaitUntilAsync(() => instance.IsCompleted);

        Assert.That(instance.IsSkippable, Is.False);
    }

    [Test]
    public void GalleryUnlockWritesGeneratedPlayerVariableWithoutAPlatformPresenter()
    {
        var gallery = GalleryCatalog.Create(new GalleryConfiguration
        {
            Types = [new GalleryTypeRegistration { TypeId = "cg", ResourceTypeId = "sprite" }],
            Items = [new GalleryItem { Id = 3, TypeId = "cg", ResourceId = "asset-cg-3" }]
        });
        using var view = new GameRegistration().AddBuiltins(gallery: gallery).BuildView();
        var runtime = BuiltinTestRuntime.Create(null);
        var instance = view.Dispatch(
            Primitive(BuiltinEntryIds.UnlockGallery, new { id = 3 }),
            runtime,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(instance, Is.TypeOf<ImmediatePrimitiveInstance>());
            Assert.That(instance!.IsBlocking, Is.False);
            Assert.That(instance.IsCompleted, Is.True);
            Assert.That(runtime.GetVariables(VariableScope.Player)["gallery_3_unlocked"].AsBool(), Is.True);
        });
    }

    [Test]
    public void GalleryUnlockRejectsMissingCatalogInsteadOfSilentlySucceeding()
    {
        using var view = new GameRegistration().AddBuiltins().BuildView();

        Assert.That(
            () => view.Dispatch(
                Primitive(BuiltinEntryIds.UnlockGallery, new { id = 100 }),
                BuiltinTestRuntime.Create(null),
                CancellationToken.None),
            Throws.InvalidOperationException.With.Message.Contains("GalleryCatalog"));
    }

    [Test]
    public void GalleryUnlockRejectsUnknownItemId()
    {
        using var view = new GameRegistration().AddBuiltins(gallery: GalleryCatalog.Empty).BuildView();

        Assert.That(
            () => view.Dispatch(
                Primitive(BuiltinEntryIds.UnlockGallery, new { id = 100 }),
                BuiltinTestRuntime.Create(null),
                CancellationToken.None),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("100"));
    }

    private static PrimitiveEntry Primitive(string type, object arguments, string? batchId = null) =>
        new(type, JsonSerializer.SerializeToElement(arguments), batchId);

    private static TimelineTrackDefinition Track(string handleId, string property, float from, float to, int durationFrames) => new()
    {
        HandleId = handleId,
        Property = property,
        Keys =
        {
            new TimelineKeyframeDefinition { Frame = 0, Value = from, InterpolationToNext = AnimationInterpolation.Linear },
            new TimelineKeyframeDefinition { Frame = durationFrames, Value = to }
        }
    };

    private static Dictionary<string, JsonElement> Parameters(params (string Name, object Value)[] parameters) =>
        parameters.ToDictionary(
            item => item.Name,
            item => JsonSerializer.SerializeToElement(item.Value),
            StringComparer.Ordinal);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    private sealed class RecordingAnimationPresenter : IAnimationPresenter
    {
        private readonly TaskCompletionSource<AnimationOutcome> _animation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<AnimationRequest> Animations { get; } = [];
        public List<TimelineDefinition> Timelines { get; } = [];
        public List<string> CompletedImmediately { get; } = [];

        public Task<AnimationOutcome> AnimateAsync(AnimationRequest request, CancellationToken cancellationToken)
        {
            Animations.Add(request);
            return _animation.Task.WaitAsync(cancellationToken);
        }

        public async Task<TimelinePlayResult> PlayTimelineAsync(TimelineDefinition timeline, CancellationToken cancellationToken, Func<double, Task>? beforeFrame = null)
        {
            Timelines.Add(timeline);
            if (beforeFrame is not null && timeline.LoopMode == AnimationLoopMode.Once)
                await beforeFrame(timeline.DurationFrames);
            return new TimelinePlayResult { Outcome = AnimationOutcome.Completed };
        }

        public bool CompleteAnimationImmediately(string playbackHandleId)
        {
            CompletedImmediately.Add(playbackHandleId);
            _animation.TrySetResult(AnimationOutcome.Skipped);
            return true;
        }
    }

    private sealed class LoopingAnimationPresenter : IAnimationPresenter
    {
        private Func<double, Task>? _beforeFrame;
        public Task AdvanceFrameAsync(double frame) => _beforeFrame!(frame);
        public Task<AnimationOutcome> AnimateAsync(AnimationRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(AnimationOutcome.Completed);

        public async Task<TimelinePlayResult> PlayTimelineAsync(TimelineDefinition timeline, CancellationToken cancellationToken, Func<double, Task>? beforeFrame = null)
        {
            _beforeFrame = beforeFrame;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new TimelinePlayResult { Outcome = AnimationOutcome.Completed };
        }

        public bool CompleteAnimationImmediately(string playbackHandleId) => false;
    }

    private sealed class RecordingEffectPresenter : IEffectPresenter
    {
        public List<EffectRequest> Started { get; } = [];
        public List<string> Stopped { get; } = [];

        public Task StartEffectAsync(EffectRequest request, CancellationToken cancellationToken)
        {
            Started.Add(request);
            return Task.CompletedTask;
        }

        public Task StopEffectAsync(string instanceId, CancellationToken cancellationToken)
        {
            Stopped.Add(instanceId);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingParticlePresenter : IParticlePresenter
    {
        public List<ParticleEmitterRequest> Started { get; } = [];
        public List<ParticleBurstRequest> Bursts { get; } = [];
        public List<string> Stopped { get; } = [];

        public Task StartParticleEmitterAsync(ParticleEmitterRequest request, CancellationToken cancellationToken)
        {
            Started.Add(request);
            return Task.CompletedTask;
        }

        public Task BurstParticlesAsync(ParticleBurstRequest request, CancellationToken cancellationToken)
        {
            Bursts.Add(request);
            return Task.CompletedTask;
        }

        public Task StopParticleEmitterAsync(string instanceId, CancellationToken cancellationToken)
        {
            Stopped.Add(instanceId);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingLayerPresenter : ILayerPresenter
    {
        public List<LayerRenderRequest> Shown { get; } = [];
        public void ShowLayer(LayerRenderRequest request) => Shown.Add(request);
        public void ReplaceLayer(string handleId, string assetId) { }
        public void HideLayer(string handleId) { }
        public void MoveLayer(string handleId, LayerTransform transform, float z, float durationSeconds) { }
    }

}
