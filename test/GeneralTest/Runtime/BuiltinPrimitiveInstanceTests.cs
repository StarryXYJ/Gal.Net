using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Core.Entry;
using GalNet.Core.Gallery;
using GalNet.Core.Primitives;
using GalNet.Core.Scene;
using GalNet.Core.Services;
using GalNet.Core.Variable;
using GalNet.Core.View;
using GalNet.Primitives.Builtins;
using GalNet.Runtime.Runtime;

namespace GeneralTest.Runtime;

public class BuiltinPrimitiveInstanceTests
{
    private static readonly JsonSerializerOptions AnimationJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Test]
    public void AnimationAnimateCommitsFinalStateAndCanBeSkippedThroughPresenter()
    {
        var presenter = new RecordingAnimationPresenter();
        using var view = new CompositeGameView(BuiltinEntryModules.CreateRecommended(animationPresenter: presenter));
        var runtime = new GameRuntime(null);
        var layer = runtime.SceneInstances.GetOrAdd("hero", id => new Layer { Id = id, Opacity = 0 });

        var instance = view.Dispatch(Primitive(AnimateEntry.TypeId, new
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
            Assert.That(presenter.CompletedImmediately, Is.EqualTo(new[] { "fade" }));
            Assert.That(instance.IsCompleted, Is.True);
        });
    }

    [Test]
    public async Task EffectApplyAndStopUpdateRuntimeStateAndPresenter()
    {
        var presenter = new RecordingEffectPresenter();
        using var view = new CompositeGameView(BuiltinEntryModules.CreateRecommended(effectPresenter: presenter));
        var runtime = new GameRuntime(null);
        var layer = runtime.SceneInstances.GetOrAdd("hero", id => new Layer { Id = id });

        var apply = view.Dispatch(Primitive(ApplyEffectEntry.TypeId, new
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
            Assert.That(runtime.SceneState.ActiveEffectIds, Is.EqualTo(new[] { "blur-1" }));
            Assert.That(runtime.SceneState.ActiveEffects.Single().Parameters, Does.Contain("radius"));
            Assert.That(layer.EffectInstanceIds, Is.EqualTo(new[] { "blur-1" }));
            Assert.That(presenter.Started.Single().InstanceId, Is.EqualTo("blur-1"));
        });

        var stop = view.Dispatch(Primitive(StopEffectEntry.TypeId, new { instanceId = "blur-1" }), runtime, CancellationToken.None);

        await WaitUntilAsync(() => stop!.IsCompleted);

        Assert.Multiple(() =>
        {
            Assert.That(runtime.SceneState.ActiveEffectIds, Is.Empty);
            Assert.That(runtime.SceneState.ActiveEffects, Is.Empty);
            Assert.That(layer.EffectInstanceIds, Is.Empty);
            Assert.That(presenter.Stopped, Is.EqualTo(new[] { "blur-1" }));
        });
    }

    [Test]
    public async Task ParticlePlayAndStopUpdateRuntimeStateAndPresenter()
    {
        var presenter = new RecordingParticlePresenter();
        using var view = new CompositeGameView(BuiltinEntryModules.CreateRecommended(particlePresenter: presenter));
        var runtime = new GameRuntime(null);

        var play = view.Dispatch(Primitive(PlayParticleEmitterEntry.TypeId, new
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
            Assert.That(runtime.SceneState.ActiveParticleEmitters, Has.Count.EqualTo(1));
            Assert.That(runtime.SceneState.ActiveParticleEmitters[0].Definition.ParticleTexture, Is.EqualTo("snowflake"));
            Assert.That(runtime.SceneState.ActiveParticleEmitters[0].Definition.MotionModule.GravityY, Is.EqualTo(12));
            Assert.That(runtime.SceneState.ActiveParticleEmitters[0].Definition.InitialModule.VelocityX, Is.EqualTo(new ParticleFloatRange(-28, -16)));
            Assert.That(runtime.SceneState.ActiveParticleEmitters[0].Definition.Flipbook?.FramesPerSecond, Is.EqualTo(new ParticleFloatRange(10, 14)));
            Assert.That(runtime.SceneInstances.TryGet<ParticleEmitterInstance>("snow", out _), Is.True);
            Assert.That(presenter.Started.Single().InstanceId, Is.EqualTo("snow"));
            Assert.That(presenter.Started.Single().Z, Is.EqualTo(42));
        });

        var animate = view.Dispatch(Primitive(AnimateEntry.TypeId, new
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
            Assert.That(runtime.SceneState.ActiveParticleEmitters[0].AnimationValues["emissionRate"], Is.EqualTo(18));
        });

        var stop = view.Dispatch(
            Primitive(StopParticleEmitterEntry.TypeId, new { instanceId = "snow" }),
            runtime,
            CancellationToken.None);

        await WaitUntilAsync(() => stop!.IsCompleted);

        Assert.Multiple(() =>
        {
            Assert.That(runtime.SceneState.ActiveParticleEmitters, Is.Empty);
            Assert.That(runtime.SceneInstances.TryGet<ParticleEmitterInstance>("snow", out _), Is.False);
            Assert.That(presenter.Stopped, Is.EqualTo(new[] { "snow" }));
        });
    }

    [Test]
    public async Task ParticleBurstIsFireAndForgetAndDoesNotCreateRuntimeState()
    {
        var presenter = new RecordingParticlePresenter();
        using var view = new CompositeGameView(BuiltinEntryModules.CreateRecommended(particlePresenter: presenter));
        var runtime = new GameRuntime(null);

        var burst = view.Dispatch(Primitive(BurstParticlesEntry.TypeId, new
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
            Assert.That(runtime.SceneState.ActiveParticleEmitters, Is.Empty);
            Assert.That(presenter.Bursts.Single().Count, Is.EqualTo(24));
            Assert.That(presenter.Bursts.Single().Z, Is.EqualTo(110));
            Assert.That(presenter.Bursts.Single().Definition.EmissionRate, Is.Zero);
            Assert.That(presenter.Bursts.Single().Definition.Shape?.Type, Is.EqualTo(ParticleShapeKind.Circle));
            Assert.That(presenter.Bursts.Single().Definition.Flipbook?.CyclesOverLifetime, Is.EqualTo(new ParticleFloatRange(1, 1)));
        });
    }

    [Test]
    public async Task AnimationPlanCanScheduleParticleLifetimeAndMultipleBursts()
    {
        var particles = new RecordingParticlePresenter();
        var animations = new RecordingAnimationPresenter();
        using var view = new CompositeGameView(BuiltinEntryModules.CreateRecommended(
            animationPresenter: animations,
            particlePresenter: particles));
        var runtime = new GameRuntime(null);
        var burstParameters = new
        {
            particleTexture = "spark",
            maxParticles = 12,
            shape = new { type = "point", x = 100, y = 120 }
        };
        var plan = new AnimationPlanDefinition
        {
            PlaybackHandleId = "burst-sequence",
            FrameRate = 100,
            DurationFrames = 1,
            Blocking = true,
            Events =
            {
                new AnimationPlanEventDefinition
                {
                    Frame = 0,
                    Type = PlayParticleEmitterEntry.TypeId,
                    Parameters = Parameters(
                        ("instanceId", "timed-snow"),
                        ("z", 80),
                        ("parameters", new { particleTexture = "snow", rate = 20, shape = new { type = "box", x = 320, y = 0, width = 640 } }))
                },
                new AnimationPlanEventDefinition { Frame = 0, Type = BurstParticlesEntry.TypeId, Parameters = Parameters(("count", 8), ("z", 90), ("parameters", burstParameters)) },
                new AnimationPlanEventDefinition { Frame = 1, Type = BurstParticlesEntry.TypeId, Parameters = Parameters(("count", 12), ("z", 95), ("parameters", burstParameters)) },
                new AnimationPlanEventDefinition { Frame = 1, Type = StopParticleEmitterEntry.TypeId, Parameters = Parameters(("instanceId", "timed-snow")) }
            }
        };

        var instance = view.Dispatch(Primitive(PlayAnimationPlanEntry.TypeId, new { plan }), runtime, CancellationToken.None);
        await WaitUntilAsync(() => instance!.IsCompleted);

        Assert.Multiple(() =>
        {
            Assert.That(particles.Started.Single().InstanceId, Is.EqualTo("timed-snow"));
            Assert.That(particles.Bursts.Select(request => request.Count), Is.EqualTo(new[] { 8, 12 }));
            Assert.That(particles.Stopped, Is.EqualTo(new[] { "timed-snow" }));
            Assert.That(runtime.SceneState.ActiveParticleEmitters, Is.Empty);
        });
    }

    [Test]
    public async Task SkippingAnimationPlanDoesNotBackfillFutureParticleBurst()
    {
        var runtime = new GameRuntime(null);
        var particles = new RecordingParticlePresenter();
        var animations = new RecordingAnimationPresenter();
        using var view = new CompositeGameView(BuiltinEntryModules.CreateRecommended(
            animationPresenter: animations,
            particlePresenter: particles));
        var plan = new AnimationPlanDefinition
        {
            PlaybackHandleId = "skipped-burst",
            FrameRate = 1,
            DurationFrames = 10,
            Blocking = true,
            Skippable = true,
            Events =
            {
                new AnimationPlanEventDefinition
                {
                    Frame = 10,
                    Type = BurstParticlesEntry.TypeId,
                    Parameters = Parameters(
                        ("count", 8),
                        ("parameters", new { particleTexture = "spark", shape = new { type = "point", x = 0, y = 0 } }))
                }
            }
        };

        var instance = view.Dispatch(Primitive(PlayAnimationPlanEntry.TypeId, new { plan }), runtime, CancellationToken.None)!;
        instance.Skip();
        await WaitUntilAsync(() => instance.IsCompleted);

        Assert.That(particles.Bursts, Is.Empty);
    }

    [Test]
    public async Task LoopingAnimationPlanRetriggersParticleBurstEachCycle()
    {
        var runtime = new GameRuntime(null);
        var particles = new RecordingParticlePresenter();
        var animations = new LoopingAnimationPresenter();
        using var view = new CompositeGameView(BuiltinEntryModules.CreateRecommended(
            animationPresenter: animations,
            particlePresenter: particles));
        using var scope = new CancellationTokenSource();
        var plan = new AnimationPlanDefinition
        {
            PlaybackHandleId = "repeating-burst",
            FrameRate = 100,
            DurationFrames = 2,
            LoopMode = AnimationLoopMode.Loop,
            Events =
            {
                new AnimationPlanEventDefinition
                {
                    Frame = 1,
                    Type = BurstParticlesEntry.TypeId,
                    Parameters = Parameters(
                        ("count", 4),
                        ("parameters", new { particleTexture = "spark", shape = new { type = "point", x = 0, y = 0 } }))
                }
            }
        };

        var instance = view.Dispatch(Primitive(PlayAnimationPlanEntry.TypeId, new { plan }), runtime, scope.Token)!;
        await WaitUntilAsync(() => particles.Bursts.Count >= 2);
        scope.Cancel();
        await WaitUntilAsync(() => instance.IsCompleted);

        Assert.That(particles.Bursts.Count, Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public async Task PersistentPresentationReplayRestartsEffectsAndParticlesWithSavedAnimationValues()
    {
        var runtime = new GameRuntime(null);
        var state = runtime.SceneState;
        state.ActiveEffects.Add(
            new ActiveEffectState
            {
                Id = "color-grade",
                ProgramResource = "grade-program",
                InstanceId = "grade",
                TargetHandleId = "background",
                Order = 3,
                Parameters = "{\"brightness\":0.25}",
                AnimationValues = new Dictionary<string, float>(StringComparer.Ordinal)
                {
                    ["brightness"] = 0.5f
                }
            });
        state.ActiveParticleEmitters.Add(
            new ActiveParticleEmitterState
            {
                InstanceId = "snow",
                Definition = new ParticleEmitterDefinition(
                    "snowflake",
                    EmissionRate: 72,
                    MaxParticles: 240,
                    Motion: new ParticleMotionDefinition { GravityY = 12 }),
                Z = 42,
                AnimationValues = new Dictionary<string, float>(StringComparer.Ordinal)
                {
                    ["emissionRate"] = 18,
                    ["particleScale"] = 1.5f
                }
            });
        state.ActiveAnimations.Add(
            new ActiveAnimationState
            {
                EntryType = AnimateEntry.TypeId,
                PlaybackHandleId = "snow-pulse",
                LoopMode = AnimationLoopMode.PingPong,
                Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["handleId"] = "snow",
                    ["property"] = "particleScale",
                    ["from"] = "1.5",
                    ["to"] = "2",
                    ["duration"] = "0.75",
                    ["curve"] = "EaseInOut",
                    ["blocking"] = "false",
                    ["skippable"] = "false",
                    ["blendMode"] = "Replace"
                }
            });
        var effectPresenter = new RecordingEffectPresenter();
        var particlePresenter = new RecordingParticlePresenter();
        var animationPresenter = new RecordingAnimationPresenter();
        var layerPresenter = new RecordingLayerPresenter();
        state.ActiveAnimations.Add(new ActiveAnimationState
        {
            EntryType = PlayAnimationPlanEntry.TypeId,
            PlaybackHandleId = "snow-plan",
            LoopMode = AnimationLoopMode.Loop,
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["plan"] = JsonSerializer.Serialize(new AnimationPlanDefinition
                {
                    PlaybackHandleId = "snow-plan",
                    DurationFrames = 24,
                    LoopMode = AnimationLoopMode.Loop,
                    Tracks = { Track("snow", "emissionRate", 18, 36, 24) },
                    Events =
                    {
                        new AnimationPlanEventDefinition
                        {
                            Frame = 0,
                            Type = ShowLayerEntry.TypeId,
                            Parameters = Parameters(
                                ("handleId", "ambient-overlay"),
                                ("assetId", "overlay.png"),
                                ("transform", new { }),
                                ("opacity", 1),
                                ("displayMode", "Native"))
                        }
                    }
                }, AnimationJsonOptions)
            }
        });

        await BuiltinPresentationReplay.ReplayPersistentSceneObjectsAsync(
            runtime,
            effectPresenter,
            particlePresenter,
            animationPresenter,
            layerPresenter,
            CancellationToken.None);

        var effectRequest = effectPresenter.Started.Single();
        var particleRequest = particlePresenter.Started.Single();
        var animationRequest = animationPresenter.Animations.Single();
        var animationPlan = animationPresenter.Plans.Single();
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
            Assert.That(animationPlan.PlaybackHandleId, Is.EqualTo("snow-plan"));
            Assert.That(animationPlan.LoopMode, Is.EqualTo(AnimationLoopMode.Loop));
            Assert.That(animationPlan.Tracks.Single().Keys[0].Value, Is.EqualTo(18));
            Assert.That(layerPresenter.Shown.Single().HandleId, Is.EqualTo("ambient-overlay"));
        });
    }

    [Test]
    public async Task LoopingParticleAnimationKeepsFrameZeroValueAndCompleteReplayDefinition()
    {
        var particles = new RecordingParticlePresenter();
        var animations = new RecordingAnimationPresenter();
        using var view = new CompositeGameView(BuiltinEntryModules.CreateRecommended(
            animationPresenter: animations,
            particlePresenter: particles));
        var runtime = new GameRuntime(null);

        var play = view.Dispatch(Primitive(PlayParticleEmitterEntry.TypeId, new
        {
            instanceId = "snow",
            parameters = new { particleTexture = "snowflake", rate = 72 }
        }), runtime, CancellationToken.None);
        await WaitUntilAsync(() => play!.IsCompleted);

        var loop = view.Dispatch(Primitive(AnimateEntry.TypeId, new
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

        var active = runtime.SceneState.ActiveAnimations.Single();
        Assert.Multiple(() =>
        {
            Assert.That(loop!.IsBlocking, Is.False);
            Assert.That(loop.IsCompleted, Is.False);
            Assert.That(runtime.SceneState.ActiveParticleEmitters.Single().AnimationValues["emissionRate"], Is.EqualTo(72));
            Assert.That(active.EntryType, Is.EqualTo(AnimateEntry.TypeId));
            Assert.That(active.LoopMode, Is.EqualTo(AnimationLoopMode.PingPong));
            Assert.That(active.Parameters["from"], Is.EqualTo("72"));
            Assert.That(active.Parameters["to"], Is.EqualTo("18"));
            Assert.That(active.Parameters["duration"], Is.EqualTo("1.25"));
            Assert.That(active.Parameters["curve"], Is.EqualTo("EaseInOut"));
            Assert.That(active.Parameters["blendMode"], Is.EqualTo("Replace"));
        });
    }

    [Test]
    public void LoopingAnimationPlanCommitsOnlyFrameZeroStateAndSavesCompletePlan()
    {
        using var view = new CompositeGameView(BuiltinEntryModules.CreateRecommended());
        var runtime = new GameRuntime(null);
        runtime.SceneInstances.GetOrAdd("old", id => new Layer { Id = id, AssetId = "old.png", Opacity = 1 });
        var plan = new AnimationPlanDefinition
        {
            PlaybackHandleId = "ambient-loop",
            DurationFrames = 30,
            LoopMode = AnimationLoopMode.Loop,
            Tracks = { Track("old", "opacity", 0.25f, 0.75f, 30) },
            Events =
            {
                new AnimationPlanEventDefinition
                {
                    Frame = 30,
                    Type = HideLayerEntry.TypeId,
                    Parameters = Parameters(("handleId", "old"))
                }
            }
        };

        view.Dispatch(Primitive(PlayAnimationPlanEntry.TypeId, new { plan }), runtime, CancellationToken.None);

        var active = runtime.SceneState.ActiveAnimations.Single();
        var replayedPlan = JsonSerializer.Deserialize<AnimationPlanDefinition>(active.Parameters["plan"], AnimationJsonOptions)!;
        Assert.Multiple(() =>
        {
            Assert.That(runtime.SceneInstances.TryGet<Layer>("old", out var old), Is.True);
            Assert.That(old.Opacity, Is.EqualTo(0.25f));
            Assert.That(active.LoopMode, Is.EqualTo(AnimationLoopMode.Loop));
            Assert.That(replayedPlan.PlaybackHandleId, Is.EqualTo("ambient-loop"));
            Assert.That(replayedPlan.Tracks.Single().Keys.Last().Value, Is.EqualTo(0.75f));
            Assert.That(replayedPlan.Events.Single().Type, Is.EqualTo(HideLayerEntry.TypeId));
        });
    }

    [Test]
    public async Task AnimationPlanCommitsLayerEventsAndFinalTrackValues()
    {
        var animation = new RecordingAnimationPresenter();
        var layers = new RecordingLayerPresenter();
        using var view = new CompositeGameView(BuiltinEntryModules.CreateRecommended(
            layerPresenter: layers,
            animationPresenter: animation));
        var runtime = new GameRuntime(null);
        var oldLayer = runtime.SceneInstances.GetOrAdd("old", id => new Layer { Id = id, AssetId = "old.png", Opacity = 1 });
        var plan = new AnimationPlanDefinition
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
                new AnimationPlanEventDefinition
                {
                    Frame = 0,
                    Type = ShowLayerEntry.TypeId,
                    Parameters = Parameters(
                        ("handleId", "new"),
                        ("assetId", "new.png"),
                        ("transform", new { }),
                        ("z", 4),
                        ("opacity", 0),
                        ("displayMode", "Native"))
                },
                new AnimationPlanEventDefinition
                {
                    Frame = 12,
                    Type = HideLayerEntry.TypeId,
                    Parameters = Parameters(("handleId", "old"))
                }
            }
        };

        var instance = view.Dispatch(Primitive(PlayAnimationPlanEntry.TypeId, new { plan }), runtime, CancellationToken.None);
        await WaitUntilAsync(() => animation.Plans.Count == 1);

        Assert.Multiple(() =>
        {
            Assert.That(runtime.SceneInstances.TryGet<Layer>("old", out _), Is.False);
            Assert.That(runtime.SceneInstances.TryGet<Layer>("new", out var newLayer), Is.True);
            Assert.That(newLayer.Opacity, Is.EqualTo(1));
            Assert.That(runtime.SceneState.Layers.Select(layer => layer.Id), Is.EqualTo(new[] { "new" }));
            Assert.That(instance, Is.TypeOf<AnimationPlanPrimitiveInstance>());
            Assert.That(animation.Plans.Single().PlaybackHandleId, Is.EqualTo("crossfade"));
            Assert.That(oldLayer.Opacity, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task FlowWaitBlocksUntilSkippedOrElapsed()
    {
        using var view = new CompositeGameView(BuiltinEntryModules.CreateRecommended());
        var runtime = new GameRuntime(null);

        var instance = view.Dispatch(Primitive(WaitEntry.TypeId, new { duration = 10 }, "wait"), runtime, CancellationToken.None);

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
        using var view = new CompositeGameView(BuiltinEntryModules.CreateRecommended(gallery: gallery));
        var runtime = new GameRuntime(null);
        var instance = view.Dispatch(
            Primitive(UnlockGalleryEntry.TypeId, new { id = 3 }),
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
        using var view = new CompositeGameView(BuiltinEntryModules.CreateRecommended());

        Assert.That(
            () => view.Dispatch(
                Primitive(UnlockGalleryEntry.TypeId, new { id = 100 }),
                new GameRuntime(null),
                CancellationToken.None),
            Throws.InvalidOperationException.With.Message.Contains("GalleryCatalog"));
    }

    [Test]
    public void GalleryUnlockRejectsUnknownItemId()
    {
        using var view = new CompositeGameView(BuiltinEntryModules.CreateRecommended(gallery: GalleryCatalog.Empty));

        Assert.That(
            () => view.Dispatch(
                Primitive(UnlockGalleryEntry.TypeId, new { id = 100 }),
                new GameRuntime(null),
                CancellationToken.None),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("100"));
    }

    private static PrimitiveEntry Primitive(string type, object arguments, string? batchId = null) =>
        new(type, JsonSerializer.SerializeToElement(arguments), batchId);

    private static AnimationTrackDefinition Track(string handleId, string property, float from, float to, int durationFrames) => new()
    {
        HandleId = handleId,
        Property = property,
        Keys =
        {
            new AnimationKeyframeDefinition { Frame = 0, Value = from, InterpolationToNext = AnimationInterpolation.Linear },
            new AnimationKeyframeDefinition { Frame = durationFrames, Value = to }
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
        public List<AnimationPlanDefinition> Plans { get; } = [];
        public List<string> CompletedImmediately { get; } = [];

        public Task<AnimationOutcome> AnimateAsync(AnimationRequest request, CancellationToken cancellationToken)
        {
            Animations.Add(request);
            return _animation.Task.WaitAsync(cancellationToken);
        }

        public Task<AnimationPlanPlayResult> PlayAnimationPlanAsync(AnimationPlanDefinition plan, CancellationToken cancellationToken)
        {
            Plans.Add(plan);
            return Task.FromResult(new AnimationPlanPlayResult { Outcome = AnimationOutcome.Completed });
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
        public Task<AnimationOutcome> AnimateAsync(AnimationRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(AnimationOutcome.Completed);

        public async Task<AnimationPlanPlayResult> PlayAnimationPlanAsync(AnimationPlanDefinition plan, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new AnimationPlanPlayResult { Outcome = AnimationOutcome.Completed };
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
