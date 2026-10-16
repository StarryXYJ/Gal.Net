using Avalonia.Headless;
using GeneralTest.Integration;
using GalNet.Avalonia.GameHost;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Core.Assets;
using GalNet.Rendering.Scene;

namespace GalNet.IntegrationTests;

[NonParallelizable]
public sealed class AvaloniaGameResourcesTests
{
    [Test]
    public async Task PartialPreloadFailureReleasesSuccessfulAcquisitions()
    {
        await using var ui = HeadlessUnitTestSession.StartNew(typeof(ScreenshotTestApplication));
        await ui.Dispatch<bool>(async () =>
        {
            using var texture = new SceneTexture(LayerImageFallback.MissingImage);
            using var assets = new Assets(texture);
            Assert.ThrowsAsync<IOException>(() => AvaloniaGameResources.CreateAsync(assets,
                (_, _) => Task.FromResult<IReadOnlyList<IGameFile>>([new Source("ok"), new Source("fail")]),
                (_, _) => Task.FromResult(Array.Empty<GalNet.Core.Scene.EffectProgramResource>())));
            Assert.That(assets.Released, Is.EqualTo(1));
            await Task.CompletedTask;
            return true;
        }, TestContext.CurrentContext.CancellationToken);
    }

    private sealed class Assets(SceneTexture texture) : IAssetManager
    {
        public int Released { get; private set; }
        public Task<AssetHandle<T>?> AcquireAsync<T>(string id, CancellationToken ct = default) where T : class =>
            id == "fail" ? Task.FromException<AssetHandle<T>?>(new IOException("decode failed")) :
            Task.FromResult<AssetHandle<T>?>(new(id, "sprite", (T)(object)texture, () => Released++));
        public Task<AssetHandle<T>?> AcquireByPathAsync<T>(string path, CancellationToken ct = default) where T : class => throw new NotSupportedException();
        public Task<IGameFile?> GetFileAsync(string assetId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IGameFile>> GetFilesAsync(string? typeId = null, CancellationToken ct = default) => throw new NotSupportedException();
        public void RegisterDecoder<T>(string resourceTypeId, IAssetDecoder<T> decoder) where T : class { }
        public void RegisterProvider(IAssetProvider provider) { }
        public void Dispose() { }
    }
    private sealed record Source(string Id) : IGameFile
    {
        public string Path => Id;
        public string TypeId => "sprite";
        public AssetMeta Metadata => null!;
        public long Length => 0;
        public string? Hash => null;
        public Stream OpenRead() => throw new NotSupportedException();
        public byte[] ReadAllBytes() => throw new NotSupportedException();
        public Task<byte[]> ReadAllBytesAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }
}
