using System.Text.Json;
using GalNet.Contracts.Builtins.Audio;
using GalNet.Core.Runtime;
using GalNet.Primitives.Builtins.Audio;

namespace GalNet.Media.Audio.Tests;

public sealed class AudioEffectRuntimeTests
{
    private static GameState State()
    {
        var builder = new StateCatalogBuilder();
        builder.Register<AudioTrackState>("builtin.audio.tracks", value => value.Id, AudioRuntime.ValidateTrack);
        builder.Register<AudioSEffectState>("builtin.audio.seffects", value => value.Key());
        builder.Register<AudioChannelState>("builtin.audio.channels", value => value.Key(), AudioRuntime.ValidateChannel);
        return new(builder.Build());
    }
    [Test]
    public async Task UpdatePatchesParametersWithoutRestartAndInvalidPatchDoesNotPartiallyApply()
    {
        var configuration = new AudioConfiguration([new() { Id = "same" }], [new() { Id = "same" }]);
        var host = new Host(); var state = State(); await using var runtime = new AudioRuntime(host); runtime.Bind(state);
        runtime.ApplySEffect(configuration, new("same", "one", "seffect.reverb", JsonSerializer.SerializeToElement(new { mix = .2, roomSize = .7 })));
        runtime.UpdateSEffect(configuration, "same", "one", JsonSerializer.SerializeToElement(new { mix = .4 }));
        var effect = state.Table<AudioSEffectState>().Values.Single();
        Assert.That(effect.Parameters.GetProperty("mix").GetSingle(), Is.EqualTo(.4f));
        Assert.That(effect.Parameters.GetProperty("roomSize").GetSingle(), Is.EqualTo(.7f));
        Assert.That(host.Requests.Any(request => request.Action == AudioBackendAction.Start), Is.False);
        var before = effect.Parameters.GetRawText(); var count = host.Requests.Count;
        runtime.UpdateSEffect(configuration, "same", "one", JsonSerializer.SerializeToElement(new { mix = .8, roomSize = 99 }));
        runtime.UpdateSEffect(configuration, "same", "absent", JsonSerializer.SerializeToElement(new { mix = .9 }));
        runtime.UpdateSEffect(configuration, "same", "one", JsonSerializer.Deserialize<JsonElement>("{\"mix\":0.2,\"mix\":0.3}"));
        Assert.That(state.Table<AudioSEffectState>().Values.Single().Parameters.GetRawText(), Is.EqualTo(before));
        Assert.That(host.Requests.Count, Is.EqualTo(count));
    }
    [Test]
    public async Task StopAllIsExplicitAndKeepsOtherScopesTargetsAndFutureAttachments()
    {
        var configuration = new AudioConfiguration([new() { Id = "same" }, new() { Id = "other" }], [new() { Id = "same" }]);
        var host = new Host(); var state = State(); await using var runtime = new AudioRuntime(host); runtime.Bind(state);
        foreach (var (scope, target, instance) in new[] { (SEffectScope.Track, "same", "first"), (SEffectScope.Track, "same", "named"), (SEffectScope.Bus, "same", "named"), (SEffectScope.Track, "other", "named") })
            runtime.ApplySEffect(configuration, new(target, instance, "seffect.reverb", JsonSerializer.SerializeToElement(new { }), scope));
        runtime.StopSEffect(configuration, "same", "");
        Assert.That(state.Table<AudioSEffectState>().Values.Count(), Is.EqualTo(4));
        runtime.StopSEffect(configuration, "same", "named", all: true);
        Assert.That(state.Table<AudioSEffectState>().Values.Count(), Is.EqualTo(4));
        runtime.StopSEffect(configuration, "same", "", all: true);
        Assert.That(state.Table<AudioSEffectState>().Values.Count(), Is.EqualTo(2));
        Assert.That(state.Table<AudioSEffectState>().Values.Any(effect => effect.Scope == SEffectScope.Bus), Is.True);
        runtime.ApplySEffect(configuration, new("same", "future", "seffect.reverb", JsonSerializer.SerializeToElement(new { })));
        Assert.That(state.Table<AudioSEffectState>().Values.Count(), Is.EqualTo(3));
        Assert.That(host.Requests.Count(request => request.Action == AudioBackendAction.StopSEffect), Is.EqualTo(2));
    }
    private sealed class Host : IAudioPlaybackHost
    {
        public List<AudioBackendRequest> Requests { get; } = [];
        public AudioBackendCapabilities Capabilities { get; } = new(true, true, true, true);
        public ISEffectCatalog SEffects => SEffectCatalog.Builtins;
        public event Action<AudioBackendEvent>? EventReceived { add { } remove { } }
        public Task ApplyAsync(AudioBackendRequest request, CancellationToken cancellationToken = default) { Requests.Add(request); return Task.CompletedTask; }
        public void Dispose() { }
    }
}
