using System.Collections.Concurrent;
using GalNet.Contracts.Builtins.Audio;
using GalNet.Core.Assets;

namespace GalNet.Media.Audio.Tests;

public sealed class PcmAudioLifetimeTests
{
    [Test]
    public async Task Missing_device_keeps_portable_effect_contracts_and_reports_playback_failure()
    {
        using var files = new Files();
        await using var host = new PcmAudioPlaybackHost(files, _ => new Decoder(), new UnavailableOutput());
        Assert.That(host.InitializationError, Does.Contain("unavailable"));
        Assert.That(host.SEffects.TryGet("seffect.reverb", out _), Is.True);
        Assert.That(host.Capabilities.SupportsMixer, Is.True);
        var events = new ConcurrentQueue<AudioBackendEvent>(); host.EventReceived += events.Enqueue;
        await host.ApplyAsync(new(AudioBackendAction.Configure, "", "", 0, Configuration: new([new() { Id = "test" }])));
        await host.ApplyAsync(new(AudioBackendAction.Start, "test", "voice", 1, "good"));
        Assert.That(events.Single().Kind, Is.EqualTo(AudioBackendEventKind.Failed));
        Assert.That(files.Releases, Is.Zero);
    }

    [Test]
    public async Task A_lease_release_failure_does_not_stop_other_terminal_cleanup_or_observers()
    {
        using var files = new Files(); using var output = new Output();
        var host = new PcmAudioPlaybackHost(files, _ => new Decoder(), output);
        var events = new ConcurrentQueue<AudioBackendEvent>();
        host.EventReceived += _ => throw new InvalidOperationException("Observer failed.");
        host.EventReceived += events.Enqueue;
        try
        {
            await host.ApplyAsync(new(AudioBackendAction.Configure, "", "", 0, Configuration: new([new() { Id = "test", MaxVoices = 2 }])));
            await host.ApplyAsync(new(AudioBackendAction.Start, "test", "bad", 1, "bad"));
            await host.ApplyAsync(new(AudioBackendAction.Start, "test", "good", 2, "good"));
            output.Pull();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            while (events.Count(value => value.Kind is AudioBackendEventKind.Ended or AudioBackendEventKind.Failed) < 2)
            { output.Pull(); await Task.Delay(5, timeout.Token); }
            Assert.That(files.Releases, Is.EqualTo(2));
            Assert.That(events.Single(value => value.VoiceId == "bad" && value.Kind != AudioBackendEventKind.Started).Kind, Is.EqualTo(AudioBackendEventKind.Failed));
            Assert.That(events.Single(value => value.VoiceId == "good" && value.Kind != AudioBackendEventKind.Started).Kind, Is.EqualTo(AudioBackendEventKind.Ended));
        }
        finally { Assert.ThrowsAsync<AggregateException>(async () => await host.DisposeAsync()); }
    }
    private sealed class Output : IAudioOutput
    {
        private AudioRenderCallback? _callback;
        public void Start(AudioRenderCallback render) => _callback = render;
        public void Pull() { var buffer = new float[960]; _callback!(buffer); }
        public void Dispose() => _callback = null;
    }
    private sealed class UnavailableOutput : IAudioOutput
    {
        public void Start(AudioRenderCallback render) => throw new PlatformNotSupportedException("Device unavailable.");
        public void Dispose() { }
    }
    private sealed class Decoder : IAudioPcmDecoder
    {
        private bool _read;
        public double DurationSeconds => .001;
        public int Read(Span<float> samples) { if (_read) return 0; samples[..96].Fill(.1f); _read = true; return 96; }
        public void Dispose() { }
    }
    private sealed class Files : IAssetFileMaterializer
    {
        public int Releases;
        public Task<AssetFileLease> AcquireAsync(string assetId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AssetFileLease(assetId, "audio", assetId, () => { Interlocked.Increment(ref Releases); if (assetId == "bad") throw new IOException("Release failed."); }));
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
