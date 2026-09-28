using GalNet.Core.Assets;
using GalNet.Core.Gallery;
using GalNet.Core.Services;

namespace GalNet.Storage.FileSystem;

/// <summary>Loads non-resource game content directly from a verified installed package directory.</summary>
public sealed class InstalledGameContentProvider : IGameContentProvider
{
    private readonly string _directory;
    private readonly IGalleryTypeCatalog _galleryTypes;

    public InstalledGameContentProvider(string directory, IResourceTypeCatalog resourceTypes, IGalleryTypeCatalog galleryTypes)
    {
        _directory = Path.GetFullPath(directory);
        ArgumentNullException.ThrowIfNull(resourceTypes);
        _galleryTypes = galleryTypes ?? throw new ArgumentNullException(nameof(galleryTypes));
    }

    public Task<GameContent> LoadAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var graph = GameGraphContentLoader.Load(_directory, Path.Combine("Graph", "graph.json"), Path.Combine("Graph", "groups"), cancellationToken);
        var gallery = GalleryFileLoader.LoadGenerated(Path.Combine(_directory, "gallery.json"), _galleryTypes);
        return new GameContent { Graph = graph, Gallery = gallery };
    }, cancellationToken);
}
