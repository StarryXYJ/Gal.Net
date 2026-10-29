using System.Text.Json;
using GalNet.Contracts.Builtins.Audio;
using GalNet.Core.Runtime;
using GalNet.Primitives.Builtins.Audio;

namespace GalNet.Media.Audio.Tests;

public sealed class AudioMixerRuntimeTests
{
    private static AudioConfiguration Configuration(AudioPlaybackOrder order = AudioPlaybackOrder.Sequential, AudioTrackRepeat repeat = AudioTrackRepeat.None) =>
        new([new() { Id = "a", BusId = "room", MaxQueuedItems = 10, PlaybackOrder = order, Repeat = repeat, PreserveDuringCatchUp = true }], [new() { Id = "room" }]);
    private static GameState State()
    {
        var builder = new StateCatalogBuilder();
        builder.Register<AudioTrackState>("builtin.audio.tracks", value => value.Id, AudioRuntime.ValidateTrack);
        builder.Register<AudioSEffectState>("builtin.audio.seffects", value => value.Key());
        builder.Register<AudioChannelState>("builtin.audio.channels", value => value.Key(), AudioRuntime.ValidateChannel);
        return new(builder.Build());
    }
    [Test]
    public async Task Bus_effects_and_levels_restore_before_start_without_voice_effect_copies()
    {
        var host = new Host(); var configuration = Configuration(); var state = State();
        await using var runtime = new AudioRuntime(host); runtime.Bind(state);
        runtime.ApplySEffect(configuration, new("room", "roomVerb", "seffect.reverb", JsonSerializer.Deserialize<JsonElement>("{\"roomSize\":0.7}"), SEffectScope.Bus));
        runtime.SetLevel(configuration, new("room", SEffectScope.Bus, .4f));
        runtime.Play(configuration, new("a", "music", AudioPlacement.Replace, .3f, FadeOutSeconds: .2f));
        var first = host.Requests.Single(value => value.Action == AudioBackendAction.Start);
        Assert.That(first.Gain, Is.EqualTo(.3f)); Assert.That(first.FadeOutSeconds, Is.EqualTo(.2f));
        Assert.That(host.Requests.Where(value => value.Action == AudioBackendAction.ApplySEffect).All(value => value.VoiceId == ""), Is.True);
        var restored = GameState.Restore(state.Catalog, state.Capture()); host.Requests.Clear();
        await runtime.RestoreAsync(restored, configuration, default);
        var start = host.Requests.FindIndex(value => value.Action == AudioBackendAction.Start);
        Assert.That(host.Requests.FindIndex(value => value.Action == AudioBackendAction.ApplySEffect && value.Scope == SEffectScope.Bus), Is.InRange(0, start - 1));
        Assert.That(host.Requests.FindIndex(value => value.Action == AudioBackendAction.SetLevel), Is.InRange(0, start - 1));
        Assert.That(restored.Table<AudioChannelState>().Values.Single().Gain, Is.EqualTo(.4f));
    }
    [Test]
    public async Task Catchup_discards_the_old_graph_and_projects_the_final_effects()
    {
        var host = new Host(); var configuration = Configuration();
        await using var runtime = new AudioRuntime(host); runtime.Play(configuration, new("a", "music", AudioPlacement.Replace));
        runtime.BeginCatchUp(); host.Requests.Clear();
        runtime.ApplySEffect(configuration, new("room", "verb", "seffect.reverb", JsonSerializer.Deserialize<JsonElement>("{}"), SEffectScope.Bus));
        Assert.That(host.Requests.Any(value => value.Action == AudioBackendAction.ApplySEffect), Is.False);
        runtime.EndCatchUp();
        Assert.That(host.Requests.Any(value => value.Action == AudioBackendAction.ApplySEffect && value.Scope == SEffectScope.Bus), Is.True);
        Assert.That(host.Requests.Last().Action, Is.EqualTo(AudioBackendAction.Start));
    }
    [TestCase(AudioTrackRepeat.One, "first")]
    [TestCase(AudioTrackRepeat.All, "second")]
    [TestCase(AudioTrackRepeat.None, "second")]
    public async Task Track_repeat_policy_decides_what_plays_after_an_instance_finishes(AudioTrackRepeat repeat, string next)
    {
        var host = new Host(); var configuration = Configuration(repeat: repeat);
        await using var runtime = new AudioRuntime(host);
        runtime.Play(configuration, new("a", "first", AudioPlacement.Enqueue));
        runtime.Play(configuration, new("a", "second", AudioPlacement.Enqueue));
        host.End(); Assert.That(host.Requests.Last(value => value.Action == AudioBackendAction.Start).AssetId, Is.EqualTo(next));
        if (repeat == AudioTrackRepeat.All) { host.End(); Assert.That(host.Requests.Last(value => value.Action == AudioBackendAction.Start).AssetId, Is.EqualTo("first")); }
    }
    [Test]
    public async Task Random_selection_is_reproducible_after_state_restore()
    {
        var configuration = Configuration(AudioPlaybackOrder.Random); var host = new Host(); var state = State();
        await using var runtime = new AudioRuntime(host); runtime.Bind(state); runtime.Pause(configuration, "a");
        foreach (var asset in new[] { "one", "two", "three", "four" }) runtime.Play(configuration, new("a", asset, AudioPlacement.Enqueue));
        var snapshot = state.Capture(); runtime.Resume(configuration, "a"); var original = host.Requests.Last(value => value.Action == AudioBackendAction.Start).AssetId;
        var restored = GameState.Restore(state.Catalog, snapshot); await runtime.RestoreAsync(restored, configuration, default);
        runtime.Resume(configuration, "a"); Assert.That(host.Requests.Last(value => value.Action == AudioBackendAction.Start).AssetId, Is.EqualTo(original));
    }
    [Test]
    public async Task Stop_defaults_to_the_instance_fade_out_and_explicit_zero_cuts_immediately()
    {
        var configuration = Configuration(); var host = new Host();
        await using var runtime = new AudioRuntime(host);
        runtime.Play(configuration, new("a", "one", AudioPlacement.Replace, FadeOutSeconds: .5f));
        runtime.Stop(configuration, new("a", true));
        Assert.That(host.Requests.Last(value => value.Action == AudioBackendAction.Stop).FadeSeconds, Is.EqualTo(.5f));
        runtime.Play(configuration, new("a", "two", AudioPlacement.Replace, FadeOutSeconds: .5f));
        runtime.Stop(configuration, new("a", true, 0));
        Assert.That(host.Requests.Last(value => value.Action == AudioBackendAction.Stop).FadeSeconds, Is.Zero);
    }

    private sealed class Host : IAudioPlaybackHost
    {
        public List<AudioBackendRequest> Requests { get; } = [];
        public AudioBackendCapabilities Capabilities { get; } = new(true, true, true, true);
        public ISEffectCatalog SEffects => SEffectCatalog.Builtins;
        public event Action<AudioBackendEvent>? EventReceived;
        public Task ApplyAsync(AudioBackendRequest request, CancellationToken cancellationToken = default) { Requests.Add(request); return Task.CompletedTask; }
        public void End() { var voice = Requests.Last(value => value.Action == AudioBackendAction.Start); EventReceived?.Invoke(new(AudioBackendEventKind.Ended, voice.TrackId, voice.VoiceId, voice.Generation)); }
        public void Dispose() { }
    }
}
