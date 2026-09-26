using GalNet.Core.Services;
using GalNet.Core.Assets;
using GalNet.Core.Gallery;
using GalNet.Assets.Provider;
using GalNet.Runtime.Loader;

namespace GalNet.Storage.FileSystem;

/// <summary>Loads game graph and groups from a published directory.</summary>
public sealed class DirectoryGameContentProvider : IGameContentProvider
{
    private readonly string _directory;

    public DirectoryGameContentProvider(string directory) => _directory = directory;

    public Task<GameContent> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var graph = GraphLoader.LoadFromFile(Path.Combine(_directory, "graph.json"));
            foreach (var group in graph.Nodes.OfType<GalNet.Core.Graph.Group>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = Path.Combine(_directory, $"{group.Id}.galgroup");
                if (File.Exists(path)) GalgroupLoader.LoadIntoGroup(group, path);
            }

            var resourceTypes = BuiltinResourceTypes.CreateCatalog();
            var galleryTypes = BuiltinGalleryTypes.CreateCatalog(resourceTypes);
            using var assets = new LocalFileProvider(Path.Combine(_directory, "Assets"), resourceTypes, optional: true);
            using var archive = assets.OpenArchive("assets");
            var gallery = new GalleryCatalogCompiler(resourceTypes, galleryTypes)
                .Compile(archive.AssetIds.Select(id => archive.GetAsset(id)!.Metadata));
            return new GameContent { Graph = graph, AssetRoot = _directory, Gallery = gallery };
        }, cancellationToken);
}
