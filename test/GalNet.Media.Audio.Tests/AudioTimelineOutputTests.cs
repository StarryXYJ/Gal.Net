using System.Text.Json;
using GalNet.Contracts.Builtins.Audio;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Core.Entry;
using GalNet.Media.Audio;
using GalNet.Presentation.Abstractions.View;
using GalNet.Primitives.Builtins;
using GalNet.Runtime.Runtime;

namespace GalNet.Media.Audio.Tests;

public sealed class AudioTimelineOutputTests
{
    [Test]
    public async Task TimelineFilterChangesTheRenderedPcmAndRestoresIt()
    {
        var host = new MixerHost();
        var clock = new Clock(host);
        var configuration = new AudioConfiguration([new() { Id = "bgm", BusId = "bgm" }], [new() { Id = "bgm" }]);
        await using var view = new GameRegistration().AddBuiltins(animationPresenter: clock,
            audioModule: new BuiltinAudioModule(host, configuration: configuration)).BuildView();
        var game = new GameRuntime(null, stateCatalog: view.StateCatalog);
        var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "GalNet.slnx"))) root = root.Parent;
        using var sample = JsonDocument.Parse(File.ReadAllText(Path.Combine(root!.FullName, "GameTestCase/Graph/groups/audio.rawgalgroup")));
        var entries = sample.RootElement.GetProperty("entries").EnumerateArray().ToArray();
        var filter = entries.Single(entry => entry.GetProperty("id").GetString() == "filter").GetProperty("parameters");
        var animation = entries.Single(entry => entry.GetProperty("id").GetString() == "filter-animation").GetProperty("parameters");
        view.Dispatch(new PrimitiveEntry("audio.seffect.apply", filter), game, default);
        var instance = view.Dispatch(new PrimitiveEntry("timeline.play", animation), game, default);
        await instance.PresentationCompletion;
        Assert.That(clock.Levels[1], Is.LessThan(clock.Levels[0] * .5));
        Assert.That(clock.Levels[2], Is.LessThan(clock.Levels[0] * .05));
        Assert.That(clock.Levels[3], Is.GreaterThan(clock.Levels[0] * .9));
    }

    private sealed class Clock(MixerHost host) : IAnimationPresenter
    {
        public List<double> Levels { get; } = [];
        public Task<AnimationOutcome> AnimateAsync(AnimationRequest request, CancellationToken cancellationToken) => Task.FromResult(AnimationOutcome.Completed);
        public async Task<TimelinePlayResult> PlayTimelineAsync(TimelineDefinition timeline, CancellationToken cancellationToken, Func<double, Task>? beforeFrame = null)
        {
            foreach (var frame in new[] { 0, 60, 120, 240 })
            {
                await beforeFrame!(frame);
                var buffer = new float[24000]; host.Mixer.Render(buffer);
                Levels.Add(Math.Sqrt(buffer.Skip(12000).Select(x => (double)x * x).Average()));
            }
            return new() { Outcome = AnimationOutcome.Completed };
        }
        public bool CompleteAnimationImmediately(string playbackHandleId) => true;
    }

    private sealed class MixerHost : IAudioPlaybackHost
    {
        public PcmMixer Mixer { get; } = new();
        public AudioBackendCapabilities Capabilities => new(true, true, true, true);
        public ISEffectCatalog SEffects => SEffectCatalog.Builtins;
        public event Action<AudioBackendEvent>? EventReceived { add { } remove { } }
        public Task ApplyAsync(AudioBackendRequest request, CancellationToken cancellationToken = default)
        {
            switch (request.Action)
            {
                case AudioBackendAction.ResetMixer: Mixer.Reset(); break;
                case AudioBackendAction.Configure:
                    Mixer.Configure(request.Configuration!);
                    Mixer.Add(new(new(AudioBackendAction.Start, "bgm", "voice", 1), Mixer.Target(SEffectScope.Track, "bgm"), new Sine()));
                    break;
                case AudioBackendAction.SetMix: Mixer.ApplyMix(request.Mix!); break;
                case AudioBackendAction.ApplySEffect:
                    Mixer.Target(request.Scope, request.TrackId).ApplyEffect(new(request.TrackId, request.SEffectInstanceId!, request.SEffectId!, request.SEffectParameters!.Value, request.Scope));
                    break;
            }
            return Task.CompletedTask;
        }
        public void Dispose() { }
    }
    private sealed class Sine : IAudioPcmSource
    {
        private int _frame;
        public double DurationSeconds => 0;
        public bool IsFinished => false;
        public int Read(Span<float> samples)
        {
            for (var i = 0; i < samples.Length; i += 2) samples[i] = samples[i + 1] = .1f * MathF.Sin(2 * MathF.PI * 4000 * _frame++ / PcmMixer.SampleRate);
            return samples.Length;
        }
    }
}
