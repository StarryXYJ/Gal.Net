using System.Text.Json;
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
            Types = [new GalleryTypeRegistration { TypeId = "cg", ResourceTypeName = "sprite" }],
            Items = [new GalleryItem { Id = "cg_3", TypeId = "cg", ResourceId = "asset-cg-3" }]
        });
        using var view = new CompositeGameView(BuiltinEntryModules.CreateRecommended(gallery: gallery));
        var runtime = new GameRuntime(null);
        var instance = view.Dispatch(
            Primitive(UnlockGalleryEntry.TypeId, new { id = "cg_3" }),
            runtime,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(instance, Is.TypeOf<ImmediatePrimitiveInstance>());
            Assert.That(instance!.IsBlocking, Is.False);
            Assert.That(instance.IsCompleted, Is.True);
            Assert.That(runtime.GetVariables(VariableScope.Player)["gallery_cg_3_unlocked"].AsBool(), Is.True);
        });
    }

    [Test]
    public void GalleryUnlockRejectsMissingCatalogInsteadOfSilentlySucceeding()
    {
        using var view = new CompositeGameView(BuiltinEntryModules.CreateRecommended());

        Assert.That(
            () => view.Dispatch(
                Primitive(UnlockGalleryEntry.TypeId, new { id = "portrait_0" }),
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
                Primitive(UnlockGalleryEntry.TypeId, new { id = "missing" }),
                new GameRuntime(null),
                CancellationToken.None),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("missing"));
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

    private sealed class RecordingLayerPresenter : ILayerPresenter
    {
        public void ShowLayer(LayerRenderRequest request) { }
        public void ReplaceLayer(string handleId, string assetId) { }
        public void HideLayer(string handleId) { }
        public void MoveLayer(string handleId, LayerTransform transform, float z, float durationSeconds) { }
    }

}
