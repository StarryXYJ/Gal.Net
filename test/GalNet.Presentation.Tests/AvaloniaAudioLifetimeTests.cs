using GalNet.Core.Assets;
using GalNet.Core.Audio;
using GalNet.Media.LibVlc;

namespace GeneralTest.Presentation;

public sealed class AvaloniaAudioLifetimeTests
{
    [TestCase(false), TestCase(true), Explicit("Requires GALNET_AUDIO_LIBVLC pointing to native LibVLC."), NonParallelizable]
    public async Task Stop_or_dispose_cancels_loading_before_a_voice_can_start(bool dispose)
    {
        LibVLCSharp.Shared.Core.Initialize(Environment.GetEnvironmentVariable("GALNET_AUDIO_LIBVLC")!);
        using var assets = new DelayedAssets();
        await using var host = new LibVlcAudioPlaybackHost(assets);
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
            Assert.That(events, Is.Empty, "A cancelled pending load must not start or report playback failure.");
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
