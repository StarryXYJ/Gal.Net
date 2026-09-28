using GalNet.Assets.Provider;
using GalNet.Core.Assets;
using GalNet.Core.Gallery;
using GalNet.Core.Services;

namespace GalNet.Storage.FileSystem;

/// <summary>Loads a source project whose editable graph and assets use the project directory layout.</summary>
public sealed class ProjectGameContentProvider : IGameContentProvider
{
    private readonly string _directory;
    private readonly IResourceTypeCatalog _resourceTypes;
    private readonly IGalleryTypeCatalog _galleryTypes;

    public ProjectGameContentProvider(string directory, IResourceTypeCatalog resourceTypes, IGalleryTypeCatalog galleryTypes)
    {
        _directory = Path.GetFullPath(directory);
        _resourceTypes = resourceTypes ?? throw new ArgumentNullException(nameof(resourceTypes));
        _galleryTypes = galleryTypes ?? throw new ArgumentNullException(nameof(galleryTypes));
    }

    public Task<GameContent> LoadAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var graph = GameGraphContentLoader.Load(_directory, Path.Combine("Graph", "graph.json"), Path.Combine("Graph", "groups"), cancellationToken);
        var assetsDirectory = Path.Combine(_directory, "Assets");
        using var assets = new LocalFileProvider(assetsDirectory, _resourceTypes, optional: true);
        using var archive = assets.OpenArchive("assets");
        var gallery = new GalleryCatalogCompiler(_resourceTypes, _galleryTypes)
            .Compile(archive.AssetIds.Select(id => archive.GetAsset(id)!.Metadata));
        return new GameContent { Graph = graph, AssetRoot = assetsDirectory, Gallery = gallery };
    }, cancellationToken);
}
