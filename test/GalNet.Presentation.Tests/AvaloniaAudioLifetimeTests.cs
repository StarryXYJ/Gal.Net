using GalNet.Contracts.Builtins.Assets;
using GalNet.Contracts.Builtins.Audio;
using GalNet.Core.Assets;
using GalNet.Media.Audio;
using GalNet.Media.Miniaudio;

namespace GeneralTest.Presentation;

[Platform("Win")]
public sealed class AvaloniaAudioLifetimeTests
{
    [Test, NonParallelizable]
    public async Task AudioVoicesShareExistingMaterializerAndCannotDeleteAnotherConsumersFile()
    {
        var root = Directory.CreateDirectory(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GalNet-shared-audio-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            await using (var source = new WaveFiles(3))
            using (var wave = await source.AcquireAsync("source")) File.Copy(wave.Path, System.IO.Path.Combine(root, "clip.wav"));
            await File.WriteAllTextAsync(System.IO.Path.Combine(root, "clip.wav.meta"), """{"id":"clip","type":"audio","path":"clip.wav","compress":"none"}""");
            using var assets = new GalNet.Assets.AssetManager([new GalNet.Assets.Provider.LocalFileProvider(root, BuiltinResourceTypes.CreateCatalog())]);
            await using var files = new GalNet.Assets.AssetFileMaterializer(assets);
            using var external = await files.AcquireAsync("clip");
            await using var host = CreateHost(files);
            var ended = 0;
            host.EventReceived += value => { if (value.Kind == AudioBackendEventKind.Ended) Interlocked.Increment(ref ended); };
            var first = new AudioBackendRequest(AudioBackendAction.Start, "sfx", "first", 1, "clip");
            var second = first with { VoiceId = "second", Generation = 2 };
            await Task.WhenAll(host.ApplyAsync(first), host.ApplyAsync(second));
            await host.ApplyAsync(first with { Action = AudioBackendAction.Stop });
            Assert.That(File.Exists(external.Path), Is.True);
            await host.ApplyAsync(second with { Action = AudioBackendAction.Stop });
            Assert.That(File.Exists(external.Path), Is.True, "Video/Gallery-style consumers still own this file.");
            await WaitUntilAsync(() => Volatile.Read(ref ended) == 2);
            external.Dispose(); Assert.That(File.Exists(external.Path), Is.False);
        }
        finally { Directory.Delete(root, true); }
    }

    [Test, NonParallelizable]
    public async Task CancellationAfterAcquireReturnsItsFileEvenIfTheProviderIgnoredCancellation()
    {
        using var assets = new DelayedAssets(); await using var files = new WaveFiles(1) { Block = true };
        await using var host = CreateHost(files);
        var events = new System.Collections.Concurrent.ConcurrentQueue<AudioBackendEvent>(); host.EventReceived += events.Enqueue;
        var request = new AudioBackendRequest(AudioBackendAction.Start, "bgm", "pending", 1, "wave");
        var starting = host.ApplyAsync(request); await files.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await host.ApplyAsync(request with { Action = AudioBackendAction.Stop }); files.Allow.TrySetResult();
        await starting.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(File.Exists(files.Path), Is.False); Assert.That(files.Releases, Is.EqualTo(1));
        Assert.That(events.Any(value => value.Kind is AudioBackendEventKind.Started or AudioBackendEventKind.Failed), Is.False);
    }

    [TestCase(0f), TestCase(.2f), NonParallelizable]
    public async Task StoppingAVoiceReleasesItsFileAfterFadeAndReportsTerminalEvent(float fade)
    {
        using var assets = new DelayedAssets(); await using var files = new WaveFiles(3);
        await using var host = CreateHost(files);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.EventReceived += value => { if (value.Kind == AudioBackendEventKind.Ended) ended.TrySetResult(); };
        var start = new AudioBackendRequest(AudioBackendAction.Start, "bgm", "voice", 1, "wave");
        await host.ApplyAsync(start);
        await host.ApplyAsync(start with { Action = AudioBackendAction.Stop, FadeSeconds = fade });
        if (fade > 0) Assert.That(File.Exists(files.Path), Is.True, "A fading voice must retain its file.");
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(File.Exists(files.Path), Is.False); Assert.That(files.Releases, Is.EqualTo(1));
    }

    [Test, NonParallelizable]
    public async Task NaturalCompletionReleasesTheVoiceFileWithoutDisposingTheHost()
    {
        using var assets = new DelayedAssets(); await using var files = new WaveFiles(.1);
        await using var host = CreateHost(files);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.EventReceived += value => { if (value.Kind == AudioBackendEventKind.Ended) ended.TrySetResult(); };
        await host.ApplyAsync(new(AudioBackendAction.Start, "bgm", "voice", 1, "wave"));
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(File.Exists(files.Path), Is.False); Assert.That(files.Releases, Is.EqualTo(1));
    }

    private static PcmAudioPlaybackHost CreateHost(IAssetFileMaterializer files)
    {
        var host = new PcmAudioPlaybackHost(files, path => new MiniaudioDecoder(path), new TestOutput());
        host.ApplyAsync(new(AudioBackendAction.Configure, "", "", 0,
            Configuration: new([new() { Id = "bgm" }, new() { Id = "sfx", MaxVoices = 8 }]))).GetAwaiter().GetResult();
        return host;
    }
    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate()) await Task.Delay(5, timeout.Token);
    }
    private sealed class TestOutput : IAudioOutput
    {
        private readonly CancellationTokenSource _cancel = new();
        private Task? _render;
        public void Start(AudioRenderCallback callback) => _render = Task.Run(async () =>
        {
            var buffer = new float[960];
            try { while (true) { await Task.Delay(10, _cancel.Token); callback(buffer); } }
            catch (OperationCanceledException) when (_cancel.IsCancellationRequested) { }
        });
        public void Dispose() { _cancel.Cancel(); _render?.GetAwaiter().GetResult(); _cancel.Dispose(); }
    }

    private sealed class WaveFiles(double seconds) : IAssetFileMaterializer
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GalNet-audio-" + Guid.NewGuid().ToString("N") + ".wav");
        public int Releases { get; private set; }
        public bool Block { get; init; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Allow { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<AssetFileLease> AcquireAsync(string id, CancellationToken token = default)
        {
            Entered.TrySetResult(); if (Block) await Allow.Task;
            var size = (int)(8000 * seconds) * 2;
            using (var writer = new BinaryWriter(File.Create(Path)))
            {
                writer.Write("RIFF"u8); writer.Write(36 + size); writer.Write("WAVEfmt "u8); writer.Write(16);
                writer.Write((short)1); writer.Write((short)1); writer.Write(8000); writer.Write(16000);
                writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(size); writer.Write(new byte[size]);
            }
            return new AssetFileLease(id, "audio", Path, () => { Releases++; File.Delete(Path); });
        }
        public void Dispose() { if (File.Exists(Path)) File.Delete(Path); }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }

    [TestCase(false), TestCase(true), NonParallelizable]
    public async Task Stop_or_dispose_cancels_loading_before_a_voice_can_start(bool dispose)
    {
        using var assets = new DelayedAssets();
        await using var files = new GalNet.Assets.AssetFileMaterializer(assets);
        await using var host = CreateHost(files);
        Assert.That(host.InitializationError, Is.Null);
        var events = new List<AudioBackendEvent>();
        host.EventReceived += events.Add;
        var request = new AudioBackendRequest(AudioBackendAction.Start, "bgm", "voice", 1, "pending");
        var starting = host.ApplyAsync(request);
        await assets.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));

        if (dispose) await host.DisposeAsync();
        else await host.ApplyAsync(request with { Action = AudioBackendAction.Stop });
        await starting.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Multiple(() =>
        {
            Assert.That(assets.Cancelled, Is.True);
            Assert.That(events.Any(value => value.Kind is AudioBackendEventKind.Started or AudioBackendEventKind.Failed), Is.False,
                "A cancelled pending load may acknowledge Stop, but must not start or report playback failure.");
        });
    }

    private sealed class DelayedAssets : IAssetManager
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Cancelled { get; private set; }
        public async Task<IGameFile?> GetFileAsync(string assetId, CancellationToken ct = default)
        {
            Entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            catch (OperationCanceledException) { Cancelled = true; throw; }
            return null;
        }
        public Task<IReadOnlyList<IGameFile>> GetFilesAsync(string? typeId = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AssetHandle<T>?> AcquireAsync<T>(string assetId, CancellationToken ct = default) where T : class => throw new NotSupportedException();
        public Task<AssetHandle<T>?> AcquireByPathAsync<T>(string path, CancellationToken ct = default) where T : class => throw new NotSupportedException();
        public void RegisterDecoder<T>(string resourceTypeId, IAssetDecoder<T> decoder) where T : class => throw new NotSupportedException();
        public void RegisterProvider(IAssetProvider provider) => throw new NotSupportedException();
        public void Dispose() { }
    }
}
