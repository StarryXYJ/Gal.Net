using GalNet.Contracts.Builtins.Audio;
using GalNet.Core.Assets;
using GalNet.Media.Miniaudio;

namespace GalNet.Media.Audio.Tests;

public sealed class MiniaudioTests
{
    [Test]
    public void Vorbis_decoder_supports_unicode_files_and_reports_duration_for_natural_fade_out()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GalNet-混响-" + Guid.NewGuid().ToString("N") + ".ogg");
        File.Copy(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "tone.ogg"), path);
        try
        {
            using var decoder = new MiniaudioDecoder(path);
            Assert.That(decoder.DurationSeconds, Is.InRange(1.5, 2.1));
            var buffer = new float[9600];
            var count = decoder.Read(buffer);
            Assert.That(count, Is.GreaterThan(0));
            Assert.That(buffer.Any(sample => MathF.Abs(sample) > .001f), Is.True);
            long samples = count;
            while ((count = decoder.Read(buffer)) > 0) samples += count;
            Assert.That(decoder.DurationSeconds, Is.EqualTo(samples / (2d * PcmMixer.SampleRate)).Within(.01));
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void Native_decoder_reads_unicode_paths_and_converts_mono_to_48k_stereo()
    {
        using var file = new WaveFile();
        using var decoder = new MiniaudioDecoder(file.Path);
        var buffer = new float[960];
        Assert.That(decoder.DurationSeconds, Is.EqualTo(.1).Within(.002));
        Assert.That(decoder.Read(buffer), Is.EqualTo(buffer.Length));
        Assert.That(buffer.Any(sample => MathF.Abs(sample) > .001f), Is.True);
        for (var index = 0; index < buffer.Length; index += 2) Assert.That(buffer[index], Is.EqualTo(buffer[index + 1]).Within(.00001));
    }

    [Test, Explicit("Opens the platform's default audio device and plays a quiet 100 ms signal.")]
    public async Task Windows_device_renders_a_real_source_and_completes_with_its_lease_released()
    {
        using var file = new WaveFile();
        await using var host = new MiniaudioAudioPlaybackHost(file);
        Assert.That(host.InitializationError, Is.Null);
        var ended = new TaskCompletionSource<AudioBackendEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.EventReceived += value => { if (value.Kind is AudioBackendEventKind.Ended or AudioBackendEventKind.Failed) ended.TrySetResult(value); };
        await host.ApplyAsync(new(AudioBackendAction.Configure, "", "", 0, Configuration: new([new() { Id = "test", BusId = "room" }], [new() { Id = "room" }])));
        await host.ApplyAsync(new(AudioBackendAction.ApplySEffect, "room", "", 0, SEffectId: "seffect.reverb", SEffectInstanceId: "space",
            SEffectParameters: System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("{}"), Scope: SEffectScope.Bus));
        await host.ApplyAsync(new(AudioBackendAction.Start, "test", "voice", 1, "test", Gain: .01f));
        await host.ApplyAsync(new(AudioBackendAction.ApplySEffect, "test", "", 0, SEffectId: "seffect.lowPass", SEffectInstanceId: "filter",
            SEffectParameters: System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("{\"cutoffHz\":500}")));
        await Task.Delay(20);
        await host.ApplyAsync(new(AudioBackendAction.StopSEffect, "test", "", 0, SEffectInstanceId: "filter"));
        var terminal = await ended.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(terminal.Kind, Is.EqualTo(AudioBackendEventKind.Ended)); Assert.That(file.Releases, Is.EqualTo(1));
    }

    [Test]
    public void Decoder_rejects_paths_that_would_be_truncated_by_the_native_abi()
    {
        Assert.Throws<ArgumentException>(() => new MiniaudioDecoder("valid.wav\0different.wav"));
    }

    [Test, Explicit("Opens the platform's default audio device to verify native callback and suspension lifetimes.")]
    public async Task Native_output_suspends_callbacks_resumes_and_does_not_leak_a_callback_after_disposal()
    {
        var output = new MiniaudioOutput();
        var callbacks = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            output.Start(samples => { samples.Clear(); Interlocked.Increment(ref callbacks); started.TrySetResult(); });
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Throws<InvalidOperationException>(() => output.Start(samples => samples.Clear()));
            output.SuspendOutput();
            var stoppedAt = Volatile.Read(ref callbacks);
            await Task.Delay(50);
            Assert.That(Volatile.Read(ref callbacks), Is.EqualTo(stoppedAt));
            output.ResumeOutput();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (Volatile.Read(ref callbacks) == stoppedAt) await Task.Delay(5, timeout.Token);
            output.Dispose();
            stoppedAt = Volatile.Read(ref callbacks);
            await Task.Delay(50);
            Assert.That(Volatile.Read(ref callbacks), Is.EqualTo(stoppedAt));
            Assert.Throws<ObjectDisposedException>(() => output.ResumeOutput());
        }
        finally { output.Dispose(); }
    }

    private sealed class WaveFile : IAssetFileMaterializer
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GalNet-音频-" + Guid.NewGuid().ToString("N") + ".wav");
        public int Releases { get; private set; }
        public WaveFile()
        {
            using var writer = new BinaryWriter(File.Create(Path));
            writer.Write("RIFF"u8); writer.Write(36 + 1600); writer.Write("WAVEfmt "u8); writer.Write(16);
            writer.Write((short)1); writer.Write((short)1); writer.Write(8000); writer.Write(16000);
            writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(1600);
            for (var index = 0; index < 800; index++) writer.Write((short)(Math.Sin(index * 2 * Math.PI * 440 / 8000) * 3000));
        }
        public Task<AssetFileLease> AcquireAsync(string assetId, CancellationToken cancellationToken = default) => Task.FromResult(new AssetFileLease(assetId, "audio", Path, () => Releases++));
        public void Dispose() => File.Delete(Path);
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
