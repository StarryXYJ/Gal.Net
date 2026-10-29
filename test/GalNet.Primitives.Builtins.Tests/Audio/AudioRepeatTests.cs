using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Audio;
using GalNet.Core.Runtime;
using GalNet.Primitives.Builtins.Audio;

namespace GeneralTest.Runtime;

public sealed class AudioRepeatTests
{
    private static readonly AudioConfiguration Tracks = BuiltinAudioDefaults.Create();

    [Test]
    public async Task FiniteRepeatUsesNewVoicesWithoutReapplyingFadeAndCompletesAfterLastIteration()
    {
        var host = new Host(); await using var audio = new AudioRuntime(host);
        var ticket = audio.Play(Tracks, new("bgm", "music", AudioPlacement.Replace, FadeInSeconds: 1, LoopCount: 3));
        var first = host.Starts.Last(); host.End(first);
        Assert.That(host.Starts.Last().VoiceId, Is.Not.EqualTo(first.VoiceId));
        Assert.That(host.Starts.Last().FadeSeconds, Is.Zero);
        host.End(first); Assert.That(host.Starts, Has.Count.EqualTo(2), "A stale end must not consume a new iteration.");
        host.End(host.Starts.Last()); Assert.That(ticket.IsCompleted, Is.False);
        host.End(host.Starts.Last()); Assert.That(ticket.IsCompleted, Is.True);
        Assert.That(host.Starts, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task RequeueTailRotatesAFullyOccupiedQueueAndTicketCanRemoveItsPendingItem()
    {
        var tracks = new AudioConfiguration([new() { Id = "bgm", MaxVoices = 1, MaxQueuedItems = 1 }]);
        var host = new Host(); await using var audio = new AudioRuntime(host);
        var a = audio.Play(tracks, new("bgm", "a", AudioPlacement.Enqueue, Completion: AudioCompletion.RequeueTail));
        var b = audio.Play(tracks, new("bgm", "b", AudioPlacement.Enqueue, Completion: AudioCompletion.RequeueTail));
        host.End(host.Starts.Last()); host.End(host.Starts.Last()); host.End(host.Starts.Last());
        Assert.That(host.Starts.Select(request => request.AssetId), Is.EqualTo(new[] { "a", "b", "a", "b" }));
        Assert.That(a.IsCompleted || b.IsCompleted, Is.False);
        a.Stop(); Assert.That(a.IsCompleted, Is.True); Assert.That(b.IsCompleted, Is.False);
        audio.Next(tracks, "bgm"); var count = host.Starts.Count;
        host.End(host.Starts.Last()); Assert.That(host.Starts, Has.Count.EqualTo(count));
        Assert.That(b.IsCompleted, Is.True);
    }

    [Test]
    public async Task InfiniteRepeatHonorsPauseResumeAndStopWithoutRequeueingStoppedAudio()
    {
        var host = new Host(); await using var audio = new AudioRuntime(host);
        var ticket = audio.Play(Tracks, new("bgm", "music", AudioPlacement.Replace, LoopCount: 0, Completion: AudioCompletion.RequeueTail));
        host.End(host.Starts.Last()); host.End(host.Starts.Last());
        audio.Pause(Tracks, "bgm"); host.End(host.Starts.Last());
        Assert.That(host.Starts, Has.Count.EqualTo(3));
        audio.Resume(Tracks, "bgm"); Assert.That(host.Starts, Has.Count.EqualTo(4));
        var stale = host.Starts.Last(); audio.Stop(Tracks, new("bgm", true)); host.End(stale);
        Assert.That(host.Starts, Has.Count.EqualTo(4)); Assert.That(ticket.IsCompleted, Is.True);
    }

    [Test]
    public async Task FailureNeverRepeatsAndReplacementAndCatchUpIgnoreOldEvents()
    {
        var host = new Host(); await using var audio = new AudioRuntime(host);
        var first = audio.Play(Tracks, new("bgm", "broken", AudioPlacement.Replace, LoopCount: 0, Completion: AudioCompletion.RequeueTail));
        audio.Play(Tracks, new("bgm", "next", AudioPlacement.Enqueue));
        host.Fail(host.Starts.Last()); Assert.That(first.IsCompleted, Is.True);
        Assert.That(host.Starts.Last().AssetId, Is.EqualTo("next"));
        var stale = host.Starts.Last(); audio.BeginCatchUp(); host.End(stale);
        audio.Play(Tracks, new("bgm", "final", AudioPlacement.Replace, LoopCount: 0));
        audio.EndCatchUp(); host.End(stale);
        Assert.That(host.Starts.Last().AssetId, Is.EqualTo("final"));
        Assert.That(host.Starts, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task RestoreContinuesRemainingIterationsAndLegacyItemsDefaultToOneDiscardedPlay()
    {
        var builder = new StateCatalogBuilder();
        builder.Register<AudioTrackState>("builtin.audio.tracks", value => value.Id, AudioRuntime.ValidateTrack);
        builder.Register<AudioSEffectState>("builtin.audio.seffects", value => value.Key());
        builder.Register<AudioChannelState>("builtin.audio.channels", value => value.Key(), AudioRuntime.ValidateChannel);
        var catalog = builder.Build(); var state = new GameState(catalog);
        var host = new Host(); await using var audio = new AudioRuntime(host); audio.Bind(state);
        audio.Play(Tracks, new("bgm", "music", AudioPlacement.Replace, LoopCount: 3)); host.End(host.Starts.Last());
        var restored = GameState.Restore(catalog, state.Capture());
        Assert.That(restored.Table<AudioTrackState>().Get("bgm").Active.Single().CompletedIterations, Is.EqualTo(1));
        await audio.RestoreAsync(restored, Tracks, default);
        host.End(host.Starts.Last()); host.End(host.Starts.Last());
        Assert.That(restored.Table<AudioTrackState>().Get("bgm").Active, Is.Empty);
        Assert.That(host.Starts, Has.Count.EqualTo(4));
        var legacy = JsonSerializer.Deserialize<AudioItemState>("""{"Id":"old","Request":{"TrackId":"bgm","AssetId":"old","Placement":0,"Gain":1,"FadeInSeconds":0}}""")!;
        Assert.That(legacy.Request.LoopCount, Is.EqualTo(1)); Assert.That(legacy.Request.Completion, Is.EqualTo(AudioCompletion.Discard));
        AudioRuntime.ValidateTrack(new() { Id = "bgm", Active = [legacy] });
    }

    [Test]
    public async Task FadingVoicesStillReceiveMuteAndAreStoppedByScopeResetButNeverEnterSnapshot()
    {
        var builder = new StateCatalogBuilder(); builder.Register<AudioTrackState>("builtin.audio.tracks", value => value.Id);
        builder.Register<AudioSEffectState>("builtin.audio.seffects", value => value.Key());
        builder.Register<AudioChannelState>("builtin.audio.channels", value => value.Key(), AudioRuntime.ValidateChannel);
        var state = new GameState(builder.Build()); var host = new Host(); await using var audio = new AudioRuntime(host); audio.Bind(state);
        audio.Play(Tracks, new("bgm", "music", AudioPlacement.Replace)); var first = host.Starts.Last();
        audio.Stop(Tracks, new("bgm", true, 5)); audio.ApplyMix(new() { MasterMuted = true });
        Assert.That(host.Requests.Last().Action, Is.EqualTo(AudioBackendAction.SetGain)); Assert.That(host.Requests.Last().Gain, Is.Zero);
        Assert.That(state.Table<AudioTrackState>().Get("bgm").Active, Is.Empty);
        host.End(first); var count = host.Requests.Count; audio.ApplyMix(new());
        Assert.That(host.Requests, Has.Count.EqualTo(count));
        audio.Play(Tracks, new("bgm", "other", AudioPlacement.Replace)); audio.Stop(Tracks, new("bgm", true, 5)); audio.Reset();
        Assert.That(host.Requests.Last().Action, Is.EqualTo(AudioBackendAction.Stop)); Assert.That(host.Requests.Last().FadeSeconds, Is.Zero);
    }

    [Test]
    public void InvalidRepeatDefinitionsAreRejectedWithoutStartingPlayback()
    {
        var host = new Host(); using var audio = new AudioRuntime(host);
        Assert.That(audio.Play(Tracks, new("bgm", "bad", AudioPlacement.Replace, LoopCount: -1)).IsCompleted, Is.True);
        Assert.That(host.Starts, Is.Empty);
        Assert.Throws<InvalidDataException>(() => AudioRuntime.ValidateTrack(new()
        { Id = "bgm", Active = [new() { Request = new("bgm", "bad", AudioPlacement.Replace, LoopCount: 2), CompletedIterations = 2 }] }));
    }

    private sealed class Host : IAudioPlaybackHost
    {
        public AudioBackendCapabilities Capabilities { get; } = new(true, true, true);
        public ISEffectCatalog SEffects => SEffectCatalog.Builtins;
        public List<AudioBackendRequest> Requests { get; } = [];
        public List<AudioBackendRequest> Starts => Requests.Where(request => request.Action == AudioBackendAction.Start).ToList();
        public event Action<AudioBackendEvent>? EventReceived;
        public Task ApplyAsync(AudioBackendRequest request, CancellationToken token = default) { Requests.Add(request); return Task.CompletedTask; }
        public void End(AudioBackendRequest request) => EventReceived?.Invoke(new(AudioBackendEventKind.Ended, request.TrackId, request.VoiceId, request.Generation));
        public void Fail(AudioBackendRequest request) => EventReceived?.Invoke(new(AudioBackendEventKind.Failed, request.TrackId, request.VoiceId, request.Generation));
        public void Dispose() { }
    }
}
