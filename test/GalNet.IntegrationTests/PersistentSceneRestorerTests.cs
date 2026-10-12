using GalNet.Core.Scene;
using GalNet.Core.Primitives;
using GalNet.Presentation.Abstractions.View;
using GalNet.Runtime.Runtime;
using GalNet.Primitives.Builtins;
using GalNet.Sample.Avalonia.Services;

namespace GalNet.IntegrationTests;

public sealed class PersistentSceneRestorerTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task RestoredLoopsKeepTheirDefinitionsAndWaitForPresentationCleanup(bool plan)
    {
        var runtime = new GameRuntime(null);
        runtime.SceneInstances.GetOrAdd("hero", id => new Layer { Id = id, AssetId = "image" });
        PrimitiveInstance definition = plan
            ? new AnimationPlanPrimitiveInstance(runtime, null, null, null, null,
                new AnimationPlanDefinition { PlaybackHandleId = "loop", DurationFrames = 60, LoopMode = AnimationLoopMode.Loop }, null, CancellationToken.None)
            : new AnimationPrimitiveInstance(runtime, null,
                new AnimationRequest { PlaybackHandleId = "loop", HandleId = "hero", Property = "opacity", From = .5f, To = 1, DurationSeconds = 1, LoopMode = AnimationLoopMode.Loop }, null, CancellationToken.None);
        definition.Dispatch(PrimitiveInvocationMode.CatchUp);
        var animations = new DelayedAnimationPresenter();
        var restorer = new PersistentSceneRestorer(null, null!, animations, null!);
        using var scope = new CancellationTokenSource();
        await restorer.RestoreAsync(runtime, scope.Token);

        var suspend = restorer.SuspendAsync(CancellationToken.None);
        Assert.That(suspend.IsCompleted, Is.False);
        Assert.That(runtime.SceneState.ActiveAnimations, Has.Count.EqualTo(1));
        animations.Cleaned.TrySetResult();
        await suspend.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(runtime.SceneState.ActiveAnimations, Has.Count.EqualTo(1));
        await restorer.RestoreAsync(runtime, scope.Token);
        Assert.That(animations.Starts, Is.EqualTo(2));
        scope.Cancel();
        await restorer.SuspendAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task RestoredVideoIsDetachedAndDrainedBeforeReusingItsHandle()
    {
        var runtime = new GameRuntime(null);
        runtime.SceneInstances.GetOrAdd("video", id => new Layer { Id = id, AssetId = "clip", IsVideo = true });
        var video = new DelayedVideoPresenter();
        var restorer = new PersistentSceneRestorer(null, null!, null!, null!, video);
        using var scope = new CancellationTokenSource();
        await restorer.RestoreAsync(runtime, scope.Token);

        var suspend = restorer.SuspendAsync(CancellationToken.None);
        Assert.That(video.Stops, Is.EqualTo(1));
        Assert.That(suspend.IsCompleted, Is.False, "Logical completion must not bypass presentation cleanup.");
        Assert.That(runtime.SceneState.Layers.Single().IsVideo, Is.True);
        video.Ends[0].TrySetResult();
        await suspend.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(runtime.SceneState.Layers.Single().IsVideo, Is.True);

        await restorer.RestoreAsync(runtime, scope.Token);
        Assert.That(video.Requests, Has.Count.EqualTo(2));
        Assert.That(video.Requests[1].PlaybackId, Is.Not.EqualTo(video.Requests[0].PlaybackId));
        scope.Cancel();
        await restorer.SuspendAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task CanceledSuspensionStillDrainsOldPlaybackBeforeTheNextRestore()
    {
        var runtime = new GameRuntime(null);
        runtime.SceneInstances.GetOrAdd("video", id => new Layer { Id = id, AssetId = "clip", IsVideo = true });
        var video = new DelayedVideoPresenter();
        var restorer = new PersistentSceneRestorer(null, null!, null!, null!, video);
        using var scope = new CancellationTokenSource();
        await restorer.RestoreAsync(runtime, scope.Token);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.That(async () => await restorer.SuspendAsync(canceled.Token), Throws.InstanceOf<OperationCanceledException>());

        var rebuild = restorer.RestoreAsync(runtime, scope.Token);
        Assert.That(video.Requests, Has.Count.EqualTo(1));
        video.Ends[0].TrySetResult();
        await rebuild.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(video.Requests, Has.Count.EqualTo(2));
        scope.Cancel();
        await restorer.SuspendAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
    }

    private sealed class DelayedAnimationPresenter : IAnimationPresenter
    {
        private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cleaned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Starts { get; private set; }
        public async Task<AnimationOutcome> AnimateAsync(AnimationRequest request, CancellationToken cancellationToken)
        {
            await RunAsync(cancellationToken);
            return AnimationOutcome.Skipped;
        }
        public async Task<AnimationPlanPlayResult> PlayAnimationPlanAsync(AnimationPlanDefinition plan, CancellationToken cancellationToken, Func<double, Task>? beforeFrame = null)
        {
            await RunAsync(cancellationToken);
            return new() { Outcome = AnimationOutcome.Skipped };
        }
        private async Task RunAsync(CancellationToken cancellationToken)
        {
            Starts++;
            try { await _stopped.Task.WaitAsync(cancellationToken); }
            finally { await Cleaned.Task; }
        }
        public bool CompleteAnimationImmediately(string playbackHandleId) => _stopped.TrySetResult();
    }

    private sealed class DelayedVideoPresenter : IVideoPresenter
    {
        public List<VideoRenderRequest> Requests { get; } = [];
        public List<TaskCompletionSource> Ends { get; } = [];
        public int Stops { get; private set; }
        public Task PlayVideoAsync(VideoRenderRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var end = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Ends.Add(end);
            return end.Task.WaitAsync(cancellationToken);
        }
        public void AdvanceVideo(string playbackId) { }
        public void StopVideo(string handleId) => Stops++;
    }
}
