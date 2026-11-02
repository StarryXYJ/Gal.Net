using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Audio;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Core.Entry;
using GalNet.Presentation.Abstractions.View;
using GalNet.Primitives.Builtins;
using GalNet.Runtime.Runtime;

namespace GeneralTest.Runtime;

public sealed class PresentationAudioTimelineTests
{
    private static readonly AudioConfiguration Configuration = new([new() { Id = "music", BusId = "music", PreserveDuringCatchUp = true }], [new() { Id = "music" }]);
    private static TimelineDefinition Definition() => new()
    {
        PlaybackHandleId = "audio-scene", FrameRate = 60, DurationFrames = 60, Blocking = true, Skippable = true,
        Events = [Event(0, "audio.seffect.apply", new { scope = "bus", targetId = "music", instanceId = "filter", id = "seffect.lowPass", parameters = new { cutoffHz = 20000 } }),
            Event(10, "audio.play", new { trackId = "music", asset = "sound", placement = "replace" })],
        AudioTracks = [new() { Scope = SEffectScope.Bus, TargetId = "music", InstanceId = "filter", Parameter = "cutoffHz", Keys = [new() { Frame = 0, Value = 20000 }, new() { Frame = 60, Value = 500 }] }]
    };
    private static TimelineEventDefinition Event(int frame, string type, object parameters) => new()
    { Frame = frame, Type = type, Parameters = JsonSerializer.SerializeToElement(parameters).EnumerateObject().ToDictionary(x => x.Name, x => x.Value.Clone()) };
    private static float Cutoff(GameRuntime game) => game.State.Table<AudioSEffectState>().Values.Single().Parameters.GetProperty("cutoffHz").GetSingle();

    [Test]
    public async Task Headless_loop_keeps_setup_effect_until_explicit_terminal_cleanup()
    {
        using var audio = new BuiltinAudioModule(configuration: Configuration);
        using var view = new GameRegistration().AddBuiltins(audioModule: audio).BuildView();
        var game = new GameRuntime(null, stateCatalog: view.StateCatalog);
        var timeline = Definition(); timeline.LoopMode = AnimationLoopMode.Loop; timeline.Blocking = false; timeline.Skippable = false;
        timeline.Events.RemoveAt(1); timeline.Events[0].Execution = TimelineEventExecution.Once;
        timeline.Events.Add(Event(60, "audio.seffect.stop", new { scope = "bus", targetId = "music", instanceId = "filter" }));
        timeline.Events.Last().Execution = TimelineEventExecution.OnCompletion;
        var loop = view.Dispatch(new("timeline.play", JsonSerializer.SerializeToElement(new { timeline })), game, default);
        await loop.PresentationCompletion;
        Assert.That(Cutoff(game), Is.EqualTo(20000));
        Assert.That(game.State.Table<LoopAnimationState>().Values, Has.Count.EqualTo(1));
        var stop = view.Dispatch(new("animation.stop", JsonSerializer.SerializeToElement(new { playbackHandleId = timeline.PlaybackHandleId, mode = "CompleteImmediately" })), game, default);
        await stop.PresentationCompletion;
        Assert.That(game.State.Table<AudioSEffectState>().Values, Is.Empty);
        Assert.That(game.State.Table<LoopAnimationState>().Values, Is.Empty);
    }

    [Test]
    public async Task Shared_frame_callback_samples_audio_and_reaches_a_restorable_terminal_value()
    {
        var host = new Host(); var clock = new Clock(); using var audio = new BuiltinAudioModule(host, configuration: Configuration);
        using var view = new GameRegistration().AddBuiltins(animationPresenter: clock, audioModule: audio).BuildView();
        var game = new GameRuntime(null, stateCatalog: view.StateCatalog);
        clock.Sample = frame => { if (frame == 30) Assert.That(Cutoff(game), Is.EqualTo(10250)); };
        var instance = view.Dispatch(new PrimitiveEntry("timeline.play", JsonSerializer.SerializeToElement(new { timeline = Definition() })), game, default);
        await instance.PresentationCompletion;
        Assert.That(Cutoff(game), Is.EqualTo(500));
        Assert.That(host.Requests.Count(x => x.Action == AudioBackendAction.Start), Is.EqualTo(1));
        var restored = GalNet.Core.Runtime.GameState.Restore(view.StateCatalog, game.State.Capture());
        Assert.That(restored.Table<AudioSEffectState>().Values.Single().Parameters.GetProperty("cutoffHz").GetSingle(), Is.EqualTo(500));
    }

    [Test]
    public async Task Skip_does_not_replay_future_transient_sounds_but_completes_audio_automation()
    {
        var host = new Host(); var clock = new Clock { WaitAtStart = true }; using var audio = new BuiltinAudioModule(host, configuration: Configuration);
        using var view = new GameRegistration().AddBuiltins(animationPresenter: clock, audioModule: audio).BuildView();
        var game = new GameRuntime(null, stateCatalog: view.StateCatalog);
        var instance = view.Dispatch(new PrimitiveEntry("timeline.play", JsonSerializer.SerializeToElement(new { timeline = Definition() })), game, default);
        instance.Skip(); await instance.PresentationCompletion;
        Assert.That(Cutoff(game), Is.EqualTo(500));
        Assert.That(host.Requests.Any(x => x.Action == AudioBackendAction.Start), Is.False);
    }

    private sealed class Clock : IAnimationPresenter
    {
        public bool WaitAtStart { get; init; }
        public Action<int>? Sample { get; set; }
        public Task<AnimationOutcome> AnimateAsync(AnimationRequest request, CancellationToken cancellationToken) => Task.FromResult(AnimationOutcome.Completed);
        public async Task<TimelinePlayResult> PlayTimelineAsync(TimelineDefinition timeline, CancellationToken cancellationToken, Func<double, Task>? beforeFrame = null)
        {
            if (WaitAtStart) await Task.Delay(Timeout.Infinite, cancellationToken);
            foreach (var frame in new[] { 0, 30, 60 }) { await beforeFrame!(frame); Sample?.Invoke(frame); }
            return new() { Outcome = AnimationOutcome.Completed };
        }
        public bool CompleteAnimationImmediately(string playbackHandleId) => true;
    }
    private sealed class Host : IAudioPlaybackHost
    {
        public List<AudioBackendRequest> Requests { get; } = [];
        public AudioBackendCapabilities Capabilities => new(true, true, true, true);
        public ISEffectCatalog SEffects => SEffectCatalog.Builtins;
        public event Action<AudioBackendEvent>? EventReceived { add { } remove { } }
        public Task ApplyAsync(AudioBackendRequest request, CancellationToken cancellationToken = default) { Requests.Add(request); return Task.CompletedTask; }
        public void Dispose() { }
    }
}
