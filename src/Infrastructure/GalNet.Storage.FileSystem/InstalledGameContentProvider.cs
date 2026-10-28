using GalNet.Contracts.Builtins.Gallery;
using GalNet.Core.Assets;
using GalNet.GameHost.Content;
using GalNet.Assets;

namespace GalNet.Storage.FileSystem;

/// <summary>Loads non-resource game content directly from a verified installed package directory.</summary>
public sealed class InstalledGameContentProvider : IGameContentProvider
{
    private readonly string _directory;
    private readonly GameContentLoadOptions _options;
    private readonly IGalleryTypeCatalog _galleryTypes;
    private readonly IResourceTypeCatalog _resourceTypes;

    public InstalledGameContentProvider(string directory, IResourceTypeCatalog resourceTypes, IGalleryTypeCatalog galleryTypes, GameContentLoadOptions? options = null)
    {
        _directory = Path.GetFullPath(directory);
        _options = options ?? new();
        ArgumentNullException.ThrowIfNull(resourceTypes);
        _resourceTypes = resourceTypes;
        _galleryTypes = galleryTypes ?? throw new ArgumentNullException(nameof(galleryTypes));
    }

    public Task<GameContent> LoadAsync(CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var assets = new AssetManager(GameInstallation.CreateInstalledAssetProviders(_directory, _resourceTypes));
        var graph = await GameGraphContentLoader.LoadResourcesAsync(_directory, assets, cancellationToken);
        var gallery = GalleryFileLoader.LoadGenerated(Path.Combine(_directory, "gallery.json"), _galleryTypes);
        var settings = GameProjectConfigurationLoader.Load(_directory, _options.Configurations);
        var localization = GameLocalizationLoader.Load(_directory, graph, _options.SelectDefaultCulture?.Invoke(settings), cancellationToken);
        return new GameContent { Graph = graph, Gallery = gallery, Configuration = settings, Localization = localization };
    }, cancellationToken);
}
