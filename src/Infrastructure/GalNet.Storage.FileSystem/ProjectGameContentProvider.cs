using GalNet.Assets.Provider;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.Core.Assets;
using GalNet.GameHost.Content;
using GalNet.Assets;

namespace GalNet.Storage.FileSystem;

/// <summary>Loads a source project whose editable graph and assets use the project directory layout.</summary>
public sealed class ProjectGameContentProvider : IGameContentProvider
{
    private readonly string _directory;
    private readonly GameContentLoadOptions _options;
    private readonly IResourceTypeCatalog _resourceTypes;
    private readonly IGalleryTypeCatalog _galleryTypes;

    public ProjectGameContentProvider(string directory, IResourceTypeCatalog resourceTypes, IGalleryTypeCatalog galleryTypes, GameContentLoadOptions? options = null)
    {
        _directory = Path.GetFullPath(directory);
        _options = options ?? new();
        _resourceTypes = resourceTypes ?? throw new ArgumentNullException(nameof(resourceTypes));
        _galleryTypes = galleryTypes ?? throw new ArgumentNullException(nameof(galleryTypes));
    }

    public Task<GameContent> LoadAsync(CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var storyAssets = new AssetManager([new CompiledStoryAssetProvider(_directory)]);
        var graph = await GameGraphContentLoader.LoadResourcesAsync(storyAssets, StoryResourceIds.Graph, cancellationToken);
        var assetsDirectory = Path.Combine(_directory, "Assets");
        using var assets = new LocalFileProvider(assetsDirectory, _resourceTypes, optional: true);
        using var archive = assets.OpenArchive("assets");
        var gallery = new GalleryCatalogCompiler(_resourceTypes, _galleryTypes)
            .Compile(archive.AssetIds.Select(id => archive.GetAsset(id)!.Metadata));
        var settings = GameProjectConfigurationLoader.Load(_directory, _options.Configurations);
        var localization = GameLocalizationLoader.Load(_directory, graph, _options.SelectDefaultCulture?.Invoke(settings), cancellationToken);
        return new GameContent { Graph = graph, Gallery = gallery, Configuration = settings, Localization = localization };
    }, cancellationToken);
}
