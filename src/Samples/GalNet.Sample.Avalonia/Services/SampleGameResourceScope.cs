using GalNet.Assets;
using GalNet.Core.Assets;
using GalNet.Core.Scene;
using GalNet.Rendering.Scene;
using GalNet.Runtime.Content;
using GalNet.Storage.FileSystem;

namespace GalNet.Sample.Avalonia.Services;

internal sealed class SampleGameResourceScope : IDisposable
{
    private readonly Dictionary<string, AssetHandle<SceneTexture>> _preloadedTextures;
    private bool _disposed;

    private SampleGameResourceScope(
        string gameDirectory,
        GameContent content,
        AssetManager assets,
        int spriteFileCount,
        Dictionary<string, AssetHandle<SceneTexture>> preloadedTextures,
        EffectProgramResource[] effectPrograms)
    {
        GameDirectory = gameDirectory;
        Content = content;
        Assets = assets;
        MediaFiles = new AssetFileMaterializer(assets);
        SpriteFileCount = spriteFileCount;
        _preloadedTextures = preloadedTextures;
        EffectPrograms = effectPrograms;
    }

    public string GameDirectory { get; }
    public GameContent Content { get; }
    public AssetManager Assets { get; }
    public IAssetFileMaterializer MediaFiles { get; }
    public int SpriteFileCount { get; }
    public IReadOnlyDictionary<string, AssetHandle<SceneTexture>> PreloadedTextures => _preloadedTextures;
    public IReadOnlyList<EffectProgramResource> EffectPrograms { get; }

    public static async Task<SampleGameResourceScope> OpenAsync(
        string gameDirectory,
        CancellationToken cancellationToken)
    {
        var (resourceTypes, galleryTypes) = SampleCustomization.CreateContentCatalogs();
        var installation = await GameInstallation.OpenAsync(gameDirectory, cancellationToken);
        var contentProvider = installation.CreateContentProvider(resourceTypes, galleryTypes);
        var content = await contentProvider.LoadAsync(cancellationToken);
        var assets = new AssetManager(installation.CreateAssetProviders(resourceTypes));
        var preloadedTextures = new Dictionary<string, AssetHandle<SceneTexture>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            SampleCustomization.ConfigureDecoders(assets);
            var spriteFiles = await SampleCustomization.SelectTexturePreloadsAsync(assets, cancellationToken);
            var preloadResults = await Task.WhenAll(
                spriteFiles.Select(file => assets.AcquireAsync<SceneTexture>(file.Id, cancellationToken)));
            foreach (var handle in preloadResults.OfType<AssetHandle<SceneTexture>>())
                preloadedTextures.Add(handle.AssetId, handle);

            var effectPrograms = await SampleCustomization.SelectEffectProgramsAsync(assets, cancellationToken);
            return new SampleGameResourceScope(
                installation.RootDirectory,
                content,
                assets,
                spriteFiles.Count,
                preloadedTextures,
                effectPrograms);
        }
        catch
        {
            foreach (var handle in preloadedTextures.Values) handle.Dispose();
            assets.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var handle in _preloadedTextures.Values) handle.Dispose();
        _preloadedTextures.Clear();
        MediaFiles.Dispose();
        Assets.Dispose();
    }
}
