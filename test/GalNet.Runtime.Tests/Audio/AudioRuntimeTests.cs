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

    [Test]
    public void Unsupported_seffect_is_diagnosed_without_attaching_to_the_track()
    {
        var host = new RecordingHost();
        using var runtime = new AudioRuntime(host);
        var diagnostics = new List<string>();
        runtime.Diagnostic += diagnostics.Add;
        using var document = JsonDocument.Parse("{\"cutoffHz\":800}");

        runtime.ApplySEffect(Tracks, new("bgm", "filter", "seffect.lowPass", document.RootElement.Clone()));

        Assert.That(host.Requests, Is.Empty);
        Assert.That(diagnostics, Does.Contain("Unsupported SEffect 'seffect.lowPass'."));
    }

    [Test]
    public async Task Stop_cancels_pending_start_and_async_disposal_drains_backend_commands()
    {
        var host = new DelayedStartHost();
        var runtime = new AudioRuntime(host);
        runtime.Play(Tracks, new("bgm", "pending", AudioPlacement.Replace));
        await host.StartEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        runtime.Stop(Tracks, new("bgm", true));
        await runtime.DisposeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(host.StartCancelled, Is.True);
            Assert.That(host.StartCompleted, Is.False);
            Assert.That(host.Actions, Is.EqualTo(new[] { AudioBackendAction.Start, AudioBackendAction.Stop }));
        });
    }

    [Test]
    public async Task Commands_for_one_voice_wait_for_asset_start_in_submission_order()
    {
        var host = new DelayedStartHost();
        await using var runtime = new AudioRuntime(host);
        runtime.Play(Tracks, new("bgm", "pending", AudioPlacement.Replace));
        await host.StartEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        runtime.Pause(Tracks, "bgm");
        runtime.Resume(Tracks, "bgm");
        Assert.That(host.Actions, Is.EqualTo(new[] { AudioBackendAction.Start }));

        host.AllowStart.TrySetResult();
        await host.Resumed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.That(host.Actions, Is.EqualTo(new[] { AudioBackendAction.Start, AudioBackendAction.Pause, AudioBackendAction.Resume }));
    }

    private sealed class DelayedStartHost : IAudioPlaybackHost
    {
        public AudioBackendCapabilities Capabilities { get; } = new(true, true, true);
        public ISEffectCatalog SEffects { get; } = new SEffectCatalog([]);
        public TaskCompletionSource StartEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowStart { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resumed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<AudioBackendAction> Actions { get; } = [];
        public bool StartCompleted { get; private set; }
        public bool StartCancelled { get; private set; }
        public event Action<AudioBackendEvent>? EventReceived { add { } remove { } }

        public async Task ApplyAsync(AudioBackendRequest request, CancellationToken cancellationToken = default)
        {
            Actions.Add(request.Action);
            if (request.Action == AudioBackendAction.Start)
            {
                StartEntered.TrySetResult();
                try
                {
                    await AllowStart.Task.WaitAsync(cancellationToken);
                    StartCompleted = true;
                }
                catch (OperationCanceledException) { StartCancelled = true; throw; }
            }
            if (request.Action == AudioBackendAction.Resume) Resumed.TrySetResult();
        }
        public void Dispose() { }
    }

    private sealed class RecordingHost : IAudioPlaybackHost
    {
        public AudioBackendCapabilities Capabilities { get; } = new(true, true, true);
        public ISEffectCatalog SEffects { get; } = new SEffectCatalog(
        [
            new("seffect.gain", new HashSet<SEffectScope> { SEffectScope.Track }, [new("gain", Required: true, Minimum: 0, Maximum: 1)])
        ]);
        public List<AudioBackendRequest> Requests { get; } = [];
        public event Action<AudioBackendEvent>? EventReceived
        {
            add { }
            remove { }
        }
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
