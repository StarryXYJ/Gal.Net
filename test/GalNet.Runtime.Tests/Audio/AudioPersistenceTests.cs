using System.Text.Json;
using GalNet.Core.Audio;
using GalNet.Core.Runtime;
using GalNet.Core.Settings;
using GalNet.Runtime.Audio;

namespace GeneralTest.Runtime;

public class AudioPersistenceTests
{
    private static readonly AudioConfiguration Tracks = new(
    [
        new() { Id = "bgm", GroupId = "bgm", PreserveDuringCatchUp = true, DefaultGain = .8f, MaxQueuedItems = 64, OverflowPolicy = AudioOverflowPolicy.StopOldest },
        new() { Id = "sfx", GroupId = "sfx", MaxVoices = 8 },
        new() { Id = "vocal", GroupId = "vocal", MaxQueuedItems = 64, OverflowPolicy = AudioOverflowPolicy.StopOldest }
    ], [new() { Id = "bgm" }, new() { Id = "sfx" }, new() { Id = "vocal" }]);
    private static StateCatalog Catalog()
    {
        var builder = new StateCatalogBuilder();
        builder.Register<AudioTrackState>("builtin.audio.tracks", value => value.Id, AudioRuntime.ValidateTrack);
        builder.Register<AudioSEffectState>("builtin.audio.seffects", value => AudioSEffectState.Key(value.TrackId, value.InstanceId));
        return builder.Build();
    }
    [Test]
    public async Task RestorePreservesPausedQueueEffectsAndUsesNewBackendIdentities()
    {
        var catalog = Catalog(); var state = new GameState(catalog); var host = new Host();
        await using var audio = new AudioRuntime(host); audio.Bind(state);
        audio.Play(Tracks, new("bgm", "first", AudioPlacement.Enqueue, .5f));
        audio.Play(Tracks, new("bgm", "second", AudioPlacement.Enqueue));
        audio.ApplySEffect(Tracks, new("bgm", "filter", "seffect.lowPass", JsonSerializer.SerializeToElement(new { cutoffHz = 1000 })));
        audio.Pause(Tracks, "bgm");
        var original = host.Requests.Single(value => value.Action == AudioBackendAction.Start);
        var restored = GameState.Restore(catalog, state.Capture());
        audio.ApplyMix(new AudioMixOptions { MasterGain = .5f, Groups = new Dictionary<string, AudioMixLevel> { ["bgm"] = new(.5f) } });
        await audio.RestoreAsync(restored, Tracks, default);
        Assert.That(host.Requests.Count(value => value.Action == AudioBackendAction.Start), Is.EqualTo(1), "Paused restoration must not briefly start playback.");
        audio.Reset(state); // A delayed cancellation from the old generation is harmless.
        Assert.That(restored.Table<AudioTrackState>().Get("bgm").Active, Has.Count.EqualTo(1));
        audio.Resume(Tracks, "bgm");
        var current = host.Requests.Last(value => value.Action == AudioBackendAction.Start);
        Assert.That(current.VoiceId, Is.Not.EqualTo(original.VoiceId));
        Assert.That(current.Gain, Is.EqualTo(.1f));
        Assert.That(host.Requests.Last(value => value.Action == AudioBackendAction.ApplySEffect).SEffectId, Is.EqualTo("seffect.lowPass"));
        host.End(original); // Late events cannot consume the restored item.
        Assert.That(restored.Table<AudioTrackState>().Get("bgm").Pending, Has.Count.EqualTo(1));
        host.End(current);
        Assert.That(restored.Table<AudioTrackState>().Get("bgm").Active.Single().Request.AssetId, Is.EqualTo("second"));
        Assert.That(restored.Table<AudioTrackState>().Get("bgm").Pending, Is.Empty);
        Assert.That(host.Requests.Last(value => value.Action == AudioBackendAction.ApplySEffect).SEffectId, Is.EqualTo("seffect.lowPass"));
    }
    [Test]
    public async Task HeadlessStateRoundTripsAndIdleEffectsApplyToFutureVoices()
    {
        var catalog = Catalog(); var state = new GameState(catalog);
        await using (var audio = new AudioRuntime())
        {
            audio.Bind(state);
            audio.ApplySEffect(Tracks, new("bgm", "gain", "seffect.gain", JsonSerializer.SerializeToElement(new { gain = .25f })));
            Assert.That(audio.Play(Tracks, new("bgm", "music", AudioPlacement.Enqueue)).IsCompleted, Is.True);
            audio.Stop(Tracks, new("bgm", true));
        }
        var restored = GameState.Restore(catalog, state.Capture()); var host = new Host();
        await using var playback = new AudioRuntime(host);
        await playback.RestoreAsync(restored, Tracks, default);
        Assert.That(host.Requests, Is.Empty);
        playback.Play(Tracks, new("bgm", "new-music", AudioPlacement.Enqueue));
        Assert.That(host.Requests.Last().Action, Is.EqualTo(AudioBackendAction.ApplySEffect));
        Assert.That(host.Requests.Last().SEffectInstanceId, Is.EqualTo("gain"));
    }
    [Test]
    public async Task SynchronousEndFromStopCannotPumpAnOldQueueDuringRestore()
    {
        var catalog = Catalog(); var state = new GameState(catalog); var host = new Host { EndOnStop = true };
        await using var audio = new AudioRuntime(host); audio.Bind(state);
        audio.Play(Tracks, new("bgm", "first", AudioPlacement.Enqueue));
        audio.Play(Tracks, new("bgm", "second", AudioPlacement.Enqueue));
        var restored = GameState.Restore(catalog, state.Capture());
        await audio.RestoreAsync(restored, Tracks, default);
        Assert.That(host.Requests.Where(value => value.Action == AudioBackendAction.Start).Select(value => value.AssetId),
            Is.EqualTo(new[] { "first", "first" }));
        Assert.That(restored.Table<AudioTrackState>().Get("bgm").Pending.Single().Request.AssetId, Is.EqualTo("second"));
    }
    [Test]
    public void InvalidSavedTrackEffectAndDuplicateItemsAreRejected()
    {
        var state = new GameState(Catalog());
        state.Table<AudioTrackState>().Add(new AudioTrackState { Id = "bgm" });
        state.Table<AudioSEffectState>().Add(new("missing", "effect", "seffect.gain", JsonSerializer.SerializeToElement(new { gain = .5f })));
        using var audio = new AudioRuntime();
        Assert.Throws<InvalidDataException>(() => audio.Validate(state, Tracks));
        var item = new AudioItemState { Id = "duplicate", Request = new("bgm", "asset", AudioPlacement.Enqueue) };
        Assert.Throws<InvalidDataException>(() => AudioRuntime.ValidateTrack(new AudioTrackState { Id = "bgm", Active = [item, item] }));
    }
    private sealed class Host : IAudioPlaybackHost
    {
        public AudioBackendCapabilities Capabilities { get; } = new(true, true, true);
        public ISEffectCatalog SEffects => SEffectCatalog.Builtins;
        public List<AudioBackendRequest> Requests { get; } = [];
        public bool EndOnStop { get; init; }
        public event Action<AudioBackendEvent>? EventReceived;
        public Task ApplyAsync(AudioBackendRequest request, CancellationToken cancellationToken = default)
        { Requests.Add(request); if (EndOnStop && request.Action == AudioBackendAction.Stop) End(request); return Task.CompletedTask; }
        public void End(AudioBackendRequest request) => EventReceived?.Invoke(new(AudioBackendEventKind.Ended, request.TrackId, request.VoiceId, request.Generation));
        public void Dispose() { }
    }
}
