using GalNet.Core.Assets;
using GalNet.Core.Gallery;
using GalNet.Core.Services;

namespace GalNet.Storage.FileSystem;

/// <summary>Loads non-resource game content directly from a verified installed package directory.</summary>
public sealed class InstalledGameContentProvider : IGameContentProvider
{
    private readonly string _directory;
    private readonly IGalleryTypeCatalog _galleryTypes;

    public InstalledGameContentProvider(string directory, IResourceTypeCatalog? resourceTypes = null, IGalleryTypeCatalog? galleryTypes = null)
    {
        _directory = Path.GetFullPath(directory);
        _galleryTypes = galleryTypes ?? BuiltinGalleryTypes.CreateCatalog(resourceTypes ?? BuiltinResourceTypes.CreateCatalog());
    }

    public Task<GameContent> LoadAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var graph = GameGraphContentLoader.Load(_directory, Path.Combine("Graph", "graph.json"), Path.Combine("Graph", "groups"), cancellationToken);
        var gallery = GalleryFileLoader.LoadGenerated(Path.Combine(_directory, "gallery.json"), _galleryTypes);
        return new GameContent { Graph = graph, Gallery = gallery };
    }, cancellationToken);
}
