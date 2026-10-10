using System.Text.Json;
using GalNet.Core.Entry;
using GalNet.Core.Primitives;
using GalNet.Presentation.Abstractions.View;
using GalNet.Primitives.Builtins;
using GalNet.Runtime.Runtime;

namespace GeneralTest.Runtime;

public sealed class VideoPrimitiveInstanceTests
{
    [Test]
    public async Task Blocking_video_advances_without_completing_until_playback_ends()
    {
        var presenter = new ControlledVideoPresenter();
        var runtime = new GameRuntime(null);
        using var view = new CompositeGameView(BuiltinEntryModules.CreateRecommended(videoPresenter: presenter));
        var instance = view.Dispatch(Play(), runtime, CancellationToken.None)!;
        instance.Skip();
        Assert.Multiple(() =>
        {
            Assert.That(instance.IsBlocking, Is.True);
            Assert.That(instance.IsCompleted, Is.False);
            Assert.That(presenter.Advances, Is.EqualTo(1));
            Assert.That(runtime.SceneState.Layers.Single().IsVideo, Is.True);
        });
        presenter.Ends[0].SetResult();
        await Completed(instance);
        Assert.That(runtime.SceneState.Layers, Is.Empty);
    }

    [Test]
    public async Task Old_completion_does_not_remove_replacement_and_stop_targets_only_one_handle()
    {
        var presenter = new ControlledVideoPresenter();
        var runtime = new GameRuntime(null);
        using var view = new CompositeGameView(BuiltinEntryModules.CreateRecommended(videoPresenter: presenter));
        var first = view.Dispatch(Play(false), runtime, CancellationToken.None)!;
        var second = view.Dispatch(Play(false), runtime, CancellationToken.None)!;
        var other = view.Dispatch(Play(false, "other"), runtime, CancellationToken.None)!;
        presenter.Ends[0].SetResult();
        await Completed(first);
        Assert.That(runtime.SceneState.Layers, Has.Count.EqualTo(2));
        view.Dispatch(new PrimitiveEntry("video.stop", JsonSerializer.SerializeToElement(new { handleId = "video" })), runtime, CancellationToken.None);
        await Completed(second);
        Assert.That(runtime.SceneState.Layers.Single().Id, Is.EqualTo("other"));
        other.Skip();
        Assert.That(presenter.Advances, Is.Zero);
        presenter.Ends[2].SetResult();
        await Completed(other);
    }

    [Test]
    public async Task Cancellation_and_failure_complete_and_remove_video_state()
    {
        foreach (var fail in new[] { false, true })
        {
            using var cancellation = new CancellationTokenSource();
            var presenter = new ControlledVideoPresenter();
            var runtime = new GameRuntime(null);
            using var view = new CompositeGameView(BuiltinEntryModules.CreateRecommended(videoPresenter: presenter));
            var instance = view.Dispatch(Play(), runtime, cancellation.Token)!;
            if (fail) presenter.Ends[0].SetException(new InvalidDataException("decode failure"));
            else cancellation.Cancel();
            await Completed(instance);
            Assert.That(runtime.SceneState.Layers, Is.Empty);
        }
    }

    [Test]
    public async Task Stable_video_snapshot_replays_definition_from_zero_without_playback_identity()
    {
        var presenter = new ControlledVideoPresenter();
        var runtime = new GameRuntime(null);
        using var view = new CompositeGameView(BuiltinEntryModules.CreateRecommended(videoPresenter: presenter));
        var first = view.Dispatch(Play(false), runtime, CancellationToken.None)!;
        var snapshot = runtime.CreateSnapshot();
        Assert.That(snapshot.SceneState.Layers.Single().VideoPlaybackId, Is.Null);
        var restored = new GameRuntime(null);
        restored.RestoreFrom(snapshot);
        await BuiltinPresentationReplay.ReplayPersistentSceneObjectsAsync(restored, null, null, null, null, videoPresenter: presenter);
        Assert.Multiple(() =>
        {
            Assert.That(presenter.Requests, Has.Count.EqualTo(2));
            Assert.That(presenter.Requests[1].PlaybackId, Is.Not.EqualTo(presenter.Requests[0].PlaybackId));
            Assert.That(presenter.Requests[1].SkipPoints, Is.Empty);
            Assert.That(restored.SceneState.Layers.Single().IsVideo, Is.True);
        });
        presenter.Ends.ForEach(end => end.TrySetResult());
        await Completed(first);
    }

    [TestCase("[5,3]")]
    [TestCase("[-1]")]
    [TestCase("[2,2]")]
    public void Invalid_skip_points_are_rejected(string json)
    {
        using var view = new CompositeGameView(BuiltinEntryModules.CreateRecommended());
        Assert.Throws<InvalidDataException>(() => view.Dispatch(new PrimitiveEntry("video.play",
            JsonSerializer.SerializeToElement(new { asset = "clip", skipPoints = JsonSerializer.Deserialize<JsonElement>(json) })),
            new GameRuntime(null), CancellationToken.None));
    }

    [Test]
    public void Headless_video_completes_without_leaving_a_layer()
    {
        var runtime = new GameRuntime(null);
        using var view = new CompositeGameView(BuiltinEntryModules.CreateRecommended());
        Assert.That(view.Dispatch(Play(), runtime, CancellationToken.None)!.IsCompleted, Is.True);
        Assert.That(runtime.SceneState.Layers, Is.Empty);
    }

    private static PrimitiveEntry Play(bool blocking = true, string handleId = "video") => new("video.play",
        JsonSerializer.SerializeToElement(new { asset = "clip", handleId, blocking = blocking.ToString().ToLowerInvariant(), skipPoints = new[] { 5d, 12, 20 } }));

    private static async Task Completed(PrimitiveInstance instance)
    {
        if (instance.IsCompleted) return;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        instance.Completed += _ => completion.TrySetResult();
        if (instance.IsCompleted) return;
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    private sealed class ControlledVideoPresenter : IVideoPresenter
    {
        public List<VideoRenderRequest> Requests { get; } = [];
        public List<TaskCompletionSource> Ends { get; } = [];
        public int Advances { get; private set; }
        public Task PlayVideoAsync(VideoRenderRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var end = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Ends.Add(end);
            return end.Task.WaitAsync(cancellationToken);
        }
        public void AdvanceVideo(string playbackId) => Advances++;
        public void StopVideo(string handleId)
        {
            for (var i = 0; i < Requests.Count; i++)
                if (Requests[i].Layer.HandleId == handleId) Ends[i].TrySetResult();
        }
    }
}
