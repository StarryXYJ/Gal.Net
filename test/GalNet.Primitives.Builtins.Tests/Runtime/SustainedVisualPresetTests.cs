using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Core.Entry;
using GalNet.Core.Primitives;
using GalNet.Core.Runtime;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;
using GalNet.Primitives.Builtins;
using GalNet.Runtime.Runtime;

namespace GeneralTest.Runtime;

public sealed class SustainedVisualPresetTests
{
    private static TimelineDefinition Lines(bool sustained = true) => GeneralTest.Entry.VisualPresetTests.Compile(
        BuiltinEntryIds.SpeedLinesPreset, new
        {
            playbackHandleId = "lines",
            program = "shader",
            durationFrames = 60,
            playbackMode = sustained ? "UntilStopped" : "Timed",
            blocking = false
        });
    private static PrimitiveEntry Primitive(string type, object arguments) => new(type, JsonSerializer.SerializeToElement(arguments));
    private static async Task StopAsync(CompositeGameView view, GameRuntime runtime, string id = "lines", string mode = "CompleteImmediately")
    {
        var stop = view.Dispatch(Primitive(BuiltinEntryIds.StopAnimation, new { playbackHandleId = id, mode }), runtime, CancellationToken.None)!;
        await stop.PresentationCompletion.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Immediate_stop_cleans_timed_and_sustained_lines_and_allows_reusing_the_handle(bool sustained)
    {
        var animation = new ControlledAnimation(); var effects = new Effects(); var layers = new Layers();
        using var view = new GameRegistration().AddBuiltins(animationPresenter: animation, effectPresenter: effects, layerPresenter: layers).BuildView();
        var runtime = new GameRuntime(null, stateCatalog: view.StateCatalog);
        runtime.State.Table<Layer>().Add(new Layer { Id = "hero", Transform = new LayerTransform { X = 37 } });
        runtime.State.Table<EffectInstance>().Add(new EffectInstance { Id = "unrelated", ProgramResource = "grade" });
        effects.Active.Add("unrelated");
        for (var round = 0; round < 2; round++)
        {
            var instance = view.Dispatch(Primitive(BuiltinEntryIds.PlayTimeline, new { timeline = Lines(sustained) }), runtime, CancellationToken.None)!;
            await animation.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            if (sustained)
            {
                await animation.AdvanceAsync(125);
                Assert.That(effects.Starts, Is.EqualTo(round + 1), "Loop boundaries must not recreate the shader.");
                Assert.That(layers.Active, Does.Contain("lines/visual/lines"));
                Assert.That(effects.Stops, Is.EqualTo(round));
            }
            await StopAsync(view, runtime);
            await instance.PresentationCompletion;
            Assert.Multiple(() =>
            {
                Assert.That(instance.IsCompleted, Is.True);
                Assert.That(layers.Active, Is.Empty);
                Assert.That(effects.Active, Is.EquivalentTo(new[] { "unrelated" }));
                Assert.That(runtime.State.Table<EffectInstance>().Values.Select(effect => effect.Id), Is.EqualTo(new[] { "unrelated" }));
                Assert.That(runtime.State.Table<Layer>().Values.Single().Transform.X, Is.EqualTo(37));
                Assert.That(runtime.State.Table<LoopAnimationState>().Values, Is.Empty);
                Assert.That(runtime.Scene().TryGet<AnimationPlaybackInstance>("lines", out _), Is.False);
            });
        }
    }

    [Test]
    public async Task Immediate_stop_waits_for_effect_cleanup_before_completing()
    {
        var animation = new ControlledAnimation(); var effects = new Effects { CleanupGate = new() }; var layers = new Layers();
        using var view = new GameRegistration().AddBuiltins(animationPresenter: animation, effectPresenter: effects, layerPresenter: layers).BuildView();
        var runtime = new GameRuntime(null, stateCatalog: view.StateCatalog);
        view.Dispatch(Primitive(BuiltinEntryIds.PlayTimeline, new { timeline = Lines() }), runtime, CancellationToken.None);
        var stopping = StopAsync(view, runtime);
        await effects.Stopping.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(stopping.IsCompleted, Is.False);
        effects.CleanupGate.SetResult();
        await stopping;
        Assert.That(layers.Active, Is.Empty);
        Assert.That(effects.Active, Is.Empty);
    }

    [Test]
    public async Task Stop_during_click_skip_waits_for_the_same_cleanup()
    {
        var animation = new ControlledAnimation(); var effects = new Effects { CleanupGate = new() }; var layers = new Layers();
        using var view = new GameRegistration().AddBuiltins(animationPresenter: animation, effectPresenter: effects, layerPresenter: layers).BuildView();
        var runtime = new GameRuntime(null, stateCatalog: view.StateCatalog);
        var instance = view.Dispatch(Primitive(BuiltinEntryIds.PlayTimeline, new { timeline = Lines(false) }), runtime, CancellationToken.None)!;
        instance.Skip();
        await effects.Stopping.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var stopping = StopAsync(view, runtime);
        try { Assert.That(stopping.IsCompleted, Is.False, "The playback handle must remain owned until skip cleanup finishes."); }
        finally { effects.CleanupGate.SetResult(); await instance.PresentationCompletion; await stopping; }
        Assert.That(layers.Active, Is.Empty);
    }

    [Test]
    public async Task Stop_during_async_setup_does_not_leave_a_playback_running()
    {
        var animation = new ControlledAnimation(); var effects = new Effects { SetupGate = new() }; var layers = new Layers();
        using var view = new GameRegistration().AddBuiltins(animationPresenter: animation, effectPresenter: effects, layerPresenter: layers).BuildView();
        var runtime = new GameRuntime(null, stateCatalog: view.StateCatalog);
        view.Dispatch(Primitive(BuiltinEntryIds.PlayTimeline, new { timeline = Lines() }), runtime, CancellationToken.None);
        var stopping = StopAsync(view, runtime);
        effects.SetupGate.SetResult();
        await stopping;
        Assert.That(animation.Started.Task.IsCompleted, Is.False);
        Assert.That(effects.Active, Is.Empty);
        Assert.That(layers.Active, Is.Empty);
    }

    [Test]
    public async Task After_iteration_keeps_lines_until_the_next_boundary_then_cleans_them()
    {
        var animation = new ControlledAnimation(); var effects = new Effects(); var layers = new Layers();
        using var view = new GameRegistration().AddBuiltins(animationPresenter: animation, effectPresenter: effects, layerPresenter: layers).BuildView();
        var runtime = new GameRuntime(null, stateCatalog: view.StateCatalog);
        var instance = view.Dispatch(Primitive(BuiltinEntryIds.PlayTimeline, new { timeline = Lines() }), runtime, CancellationToken.None)!;
        await animation.AdvanceAsync(65);
        await StopAsync(view, runtime, mode: "AfterIteration");
        await animation.AdvanceAsync(119);
        Assert.That(instance.IsCompleted, Is.False);
        Assert.That(effects.Active, Is.Not.Empty);
        await animation.AdvanceAsync(120);
        await instance.PresentationCompletion.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(effects.Active, Is.Empty);
        Assert.That(layers.Active, Is.Empty);
    }

    [Test]
    public async Task Catch_up_and_restoration_keep_sustained_objects_until_explicit_stop()
    {
        var animation = new ControlledAnimation(); var effects = new Effects(); var layers = new Layers();
        using var view = new GameRegistration().AddBuiltins(animationPresenter: animation, effectPresenter: effects, layerPresenter: layers).BuildView();
        var runtime = new GameRuntime(null, stateCatalog: view.StateCatalog);
        var definition = new TimelinePrimitiveInstance(runtime, null, null, null, null, Lines(), null, CancellationToken.None);
        definition.Dispatch(PrimitiveInvocationMode.CatchUp);
        var restored = GameState.Restore(view.StateCatalog, runtime.State.Capture());
        Assert.That(restored.Table<LoopAnimationState>().Values.Single().Timeline!.Events.Count(item => item.Execution == TimelineEventExecution.OnCompletion), Is.EqualTo(2));
        await using var restorer = new StateRestoreSession(view);
        await restorer.RestoreAsync(runtime);
        await animation.AdvanceAsync(125);
        Assert.That(effects.Starts, Is.EqualTo(1), "Restoration owns setup; replay must not create it again.");
        await StopAsync(view, runtime);
        Assert.That(effects.Active, Is.Empty);
        Assert.That(layers.Active, Is.Empty);
        Assert.That(runtime.State.Table<EffectInstance>().Values, Is.Empty);
        Assert.That(runtime.State.Table<Layer>().Values, Is.Empty);
    }

    [Test]
    public async Task Headless_stop_removes_saved_loop_and_its_helpers()
    {
        using var view = new GameRegistration().AddBuiltins().BuildView();
        var runtime = new GameRuntime(null, stateCatalog: view.StateCatalog);
        view.Dispatch(Primitive(BuiltinEntryIds.PlayTimeline, new { timeline = Lines() }), runtime, CancellationToken.None);
        Assert.That(runtime.State.Table<EffectInstance>().Values, Has.Count.EqualTo(1));
        await StopAsync(view, runtime);
        Assert.That(runtime.State.Table<EffectInstance>().Values, Is.Empty);
        Assert.That(runtime.State.Table<Layer>().Values, Is.Empty);
        Assert.That(runtime.State.Table<LoopAnimationState>().Values, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Completion_and_skip_run_cleanup_without_a_separate_stop(bool skip)
    {
        var animation = new ControlledAnimation(); var effects = new Effects(); var layers = new Layers();
        using var view = new GameRegistration().AddBuiltins(animationPresenter: animation, effectPresenter: effects, layerPresenter: layers).BuildView();
        var runtime = new GameRuntime(null, stateCatalog: view.StateCatalog);
        var instance = view.Dispatch(Primitive(BuiltinEntryIds.PlayTimeline, new { timeline = Lines(false) }), runtime, CancellationToken.None)!;
        if (skip) instance.Skip(); else animation.Finish(AnimationOutcome.Completed);
        await instance.PresentationCompletion.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(effects.Active, Is.Empty);
        Assert.That(layers.Active, Is.Empty);
        Assert.That(effects.Stops, Is.EqualTo(1));
    }

    private sealed class ControlledAnimation : IAnimationPresenter
    {
        private TaskCompletionSource<AnimationOutcome> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Func<double, Task>? _frame;
        public TaskCompletionSource Started { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task AdvanceAsync(double frame) => _frame!(frame);
        public void Finish(AnimationOutcome outcome) => _completion.TrySetResult(outcome);
        public Task<AnimationOutcome> AnimateAsync(AnimationRequest request, CancellationToken cancellationToken) => _completion.Task.WaitAsync(cancellationToken);
        public async Task<TimelinePlayResult> PlayTimelineAsync(TimelineDefinition timeline, CancellationToken cancellationToken, Func<double, Task>? beforeFrame = null)
        {
            _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _frame = beforeFrame;
            Started.TrySetResult();
            try { return new() { Outcome = await _completion.Task.WaitAsync(cancellationToken) }; }
            finally { Started = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        }
        public bool CompleteAnimationImmediately(string playbackHandleId) => _completion.TrySetResult(AnimationOutcome.Skipped);
    }
    private sealed class Effects : IEffectPresenter
    {
        public HashSet<string> Active { get; } = [];
        public int Starts { get; private set; }
        public int Stops { get; private set; }
        public TaskCompletionSource? SetupGate { get; init; }
        public TaskCompletionSource? CleanupGate { get; init; }
        public TaskCompletionSource Stopping { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task StartEffectAsync(EffectRequest request, CancellationToken cancellationToken)
        {
            if (SetupGate is not null) await SetupGate.Task.WaitAsync(cancellationToken);
            Active.Add(request.InstanceId); Starts++;
        }
        public async Task StopEffectAsync(string instanceId, CancellationToken cancellationToken)
        {
            Stopping.TrySetResult();
            if (CleanupGate is not null) await CleanupGate.Task.WaitAsync(cancellationToken);
            Active.Remove(instanceId); Stops++;
        }
    }
    private sealed class Layers : ILayerPresenter
    {
        public HashSet<string> Active { get; } = [];
        public void ShowLayer(LayerRenderRequest request) => Active.Add(request.HandleId);
        public void HideLayer(string handleId) => Active.Remove(handleId);
        public void ReplaceLayer(string handleId, string assetId) { }
        public void MoveLayer(string handleId, LayerTransform transform, float z, float durationSeconds) { }
    }
}
