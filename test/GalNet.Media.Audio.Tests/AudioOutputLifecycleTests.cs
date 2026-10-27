using System.Collections.Concurrent;
using GalNet.Contracts.Builtins.Audio;
using GalNet.Core.Assets;

namespace GalNet.Media.Audio.Tests;

public sealed class AudioOutputLifecycleTests
{
    [Test]
    public async Task Suspending_output_retains_voices_leases_and_story_pause_state_until_resume()
    {
        var files = new Files();
        var output = new Output();
        await using var host = new PcmAudioPlaybackHost(files, _ => new Decoder(), output);
        var events = new ConcurrentQueue<AudioBackendEvent>();
        host.EventReceived += events.Enqueue;
        await host.ApplyAsync(new(AudioBackendAction.Configure, "", "", 0, Configuration: new([new() { Id = "test" }])));
        await host.ApplyAsync(new(AudioBackendAction.Start, "test", "voice", 1, "clip"));
        Assert.That(host.SupportsOutputSuspension, Is.True);
        host.SuspendOutput();
        host.SuspendOutput();
        Assert.That(output.Suspends, Is.EqualTo(1));
        Assert.That(output.Pull().All(sample => sample == 0), Is.True);
        Assert.That(files.Releases, Is.Zero);
        Assert.That(events.Select(value => value.Kind), Is.EqualTo(new[] { AudioBackendEventKind.Started }));

        // A story pause is independent of a system interruption.
        await host.ApplyAsync(new(AudioBackendAction.Pause, "test", "voice", 1));
        host.ResumeOutput();
        host.ResumeOutput();
        Assert.That(output.Resumes, Is.EqualTo(1));
        Assert.That(output.Pull().All(sample => sample == 0), Is.True);
        await host.ApplyAsync(new(AudioBackendAction.Resume, "test", "voice", 1));
        Assert.That(output.Pull().Any(sample => sample > 0), Is.True);
        await host.DisposeAsync();
        Assert.That(files.Releases, Is.EqualTo(1));
        Assert.That(output.Disposed, Is.True);
        Assert.Throws<ObjectDisposedException>(() => host.ResumeOutput());
    }

    [Test]
    public async Task Failed_resume_can_be_retried_without_changing_the_suspended_state()
    {
        var output = new Output { FailResume = true };
        await using var host = new PcmAudioPlaybackHost(new Files(), _ => new Decoder(), output);
        host.SuspendOutput();
        Assert.Throws<IOException>(() => host.ResumeOutput());
        Assert.That(output.Suspended, Is.True);
        output.FailResume = false;
        host.ResumeOutput();
        Assert.That(output.Suspended, Is.False);
        Assert.That(output.Resumes, Is.EqualTo(1));
    }

    [Test]
    public async Task Outputs_without_lifecycle_support_report_the_capability_and_reject_suspension()
    {
        await using var host = new PcmAudioPlaybackHost(new Files(), _ => new Decoder(), new SimpleOutput());
        Assert.That(host.SupportsOutputSuspension, Is.False);
        Assert.Throws<NotSupportedException>(() => host.SuspendOutput());
    }

    private sealed class Output : IAudioOutput, IAudioOutputLifecycle
    {
        private AudioRenderCallback? _render;
        public int Suspends { get; private set; }
        public int Resumes { get; private set; }
        public bool Suspended { get; private set; }
        public bool Disposed { get; private set; }
        public bool FailResume { get; set; }
        public void Start(AudioRenderCallback render) => _render = render;
        public void SuspendOutput() { Suspends++; Suspended = true; }
        public void ResumeOutput() { if (FailResume) throw new IOException("Device temporarily unavailable."); Resumes++; Suspended = false; }
        public float[] Pull() { var samples = new float[960]; if (!Suspended) _render!(samples); return samples; }
        public void Dispose() { Disposed = true; _render = null; }
    }

    private sealed class SimpleOutput : IAudioOutput
    {
        public void Start(AudioRenderCallback render) { }
        public void Dispose() { }
    }

    private sealed class Decoder : IAudioPcmDecoder
    {
        public double DurationSeconds => 10;
        public int Read(Span<float> samples) { samples.Fill(.25f); return samples.Length; }
        public void Dispose() { }
    }

    private sealed class Files : IAssetFileMaterializer
    {
        public int Releases;
        public Task<AssetFileLease> AcquireAsync(string assetId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AssetFileLease(assetId, "audio", assetId, () => Interlocked.Increment(ref Releases)));
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
