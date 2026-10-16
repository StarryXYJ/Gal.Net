using Avalonia.Headless;
using GalNet.Avalonia.GameHost;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Core.Assets;
using GalNet.Rendering.Scene;
using GeneralTest.Integration;

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

    [Test]
    public async Task DefaultPresentationUsesAndReleasesInjectedAdapters()
    {
        await using var ui = HeadlessUnitTestSession.StartNew(typeof(ScreenshotTestApplication));
        await ui.Dispatch<bool>(async () =>
        {
            using var texture = new SceneTexture(LayerImageFallback.MissingImage);
            using var assets = new Assets(texture);
            using var files = new GalNet.Assets.AssetFileMaterializer(assets);
            var audio = new Audio();
            var layers = new Layers(texture);
            var options = new AvaloniaGamePresentationOptions
            {
                CreateLayers = _ => layers,
                CreateAudioHost = (_, _, _) => audio
            };
            var presentation = await AvaloniaGamePresentation.CreateAsync(
                new GalNet.Avalonia.GameView.ViewModels.GamePageViewModel(null!),
                new GalNet.Avalonia.GameView.Page.GamePage(), assets,
                new Dictionary<string, AssetHandle<SceneTexture>>(), [], files,
                (registration, _) => registration.RegisterSchema(new GalNet.Core.Entry.DefaultEntryModule("custom")), [], options);
            Assert.That(presentation.Audio, Is.SameAs(audio));
            var closing = presentation.DisposeAsync().AsTask();
            await closing;
            Assert.That(presentation.DisposeAsync().AsTask(), Is.SameAs(closing));
            Assert.That(audio.Disposals, Is.EqualTo(1));
            Assert.That(layers.Disposals, Is.EqualTo(1));
            return true;
        }, TestContext.CurrentContext.CancellationToken);
    }

    private sealed class Layers(SceneTexture texture) : IGamePageLayerFactory, IDisposable
    {
        public int Disposals { get; private set; }
        public SceneTexture ResolveTexture(string assetId) => texture;
        public void Dispose() => Disposals++;
    }
    private sealed class Audio : GalNet.Presentation.Abstractions.Audio.IAudioPlaybackHost
    {
        public int Disposals { get; private set; }
        public GalNet.Core.Audio.AudioBackendCapabilities Capabilities => GalNet.Core.Audio.AudioBackendCapabilities.None;
        public GalNet.Core.Audio.ISEffectCatalog SEffects => GalNet.Core.Audio.SEffectCatalog.Empty;
        public event Action<GalNet.Core.Audio.AudioBackendEvent>? EventReceived { add { } remove { } }
        public Task ApplyAsync(GalNet.Core.Audio.AudioBackendRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() => Disposals++;
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
