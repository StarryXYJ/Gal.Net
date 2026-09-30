using System.Text.Json;
using GalNet.Core.Audio;
using GalNet.Presentation.Abstractions.Audio;
using GalNet.Runtime.Audio;

namespace GeneralTest.Runtime;

public class AudioRuntimeTests
{
    private static readonly IReadOnlyList<AudioTrackDefinition> Tracks =
    [new() { Id = "bgm", Bus = AudioBuses.Bgm, MaxVoices = 1, MaxQueuedItems = 2, DefaultGain = 0.8f }];

    [Test]
    public async Task Replace_stops_current_voice_then_starts_the_new_asset()
    {
        var host = new RecordingHost();
        using var runtime = new AudioRuntime(host);
        runtime.Play(Tracks, new("bgm", "first", AudioPlacement.Replace, 1));
        runtime.Play(Tracks, new("bgm", "second", AudioPlacement.Replace, .5f, 1));

        await host.WaitForCountAsync(3);

        Assert.That(host.Requests.Select(request => request.Action), Is.EqualTo([AudioBackendAction.Start, AudioBackendAction.Stop, AudioBackendAction.Start]));
        Assert.That(host.Requests.Last().AssetId, Is.EqualTo("second"));
        Assert.That(host.Requests.Last().Gain, Is.EqualTo(.4f));
        Assert.That(host.Requests.Last().FadeSeconds, Is.EqualTo(1));
    }

    [Test]
    public async Task Active_seffect_is_sent_to_current_and_future_voices_and_can_be_removed()
    {
        var host = new RecordingHost();
        using var runtime = new AudioRuntime(host);
        runtime.Play(Tracks, new("bgm", "first", AudioPlacement.Replace));
        await host.WaitForCountAsync(1);
        using var document = JsonDocument.Parse("{\"gain\":0.4}");

        runtime.ApplySEffect(Tracks, new("bgm", "duck", "seffect.gain", document.RootElement.Clone()));
        runtime.StopSEffect(Tracks, "bgm", "duck");
        await host.WaitForCountAsync(3);

        Assert.That(host.Requests.Skip(1).Select(request => request.Action), Is.EqualTo([AudioBackendAction.ApplySEffect, AudioBackendAction.StopSEffect]));
        Assert.That(host.Requests[1].SEffectInstanceId, Is.EqualTo("duck"));
    }

    private sealed class RecordingHost : IAudioPlaybackHost
    {
        public AudioBackendCapabilities Capabilities { get; } = new(true, true, true);
        public List<AudioBackendRequest> Requests { get; } = [];
        public event Action<AudioBackendEvent>? EventReceived;
        public Task ApplyAsync(AudioBackendRequest request, CancellationToken cancellationToken = default)
        {
            lock (Requests) Requests.Add(request);
            return Task.CompletedTask;
        }
        public async Task WaitForCountAsync(int count)
        {
            for (var attempt = 0; attempt < 50; attempt++)
            {
                lock (Requests) if (Requests.Count >= count) return;
                await Task.Delay(10);
            }
            Assert.Fail($"Expected {count} backend requests but received {Requests.Count}.");
        }
        public void Dispose() { }
    }
}
