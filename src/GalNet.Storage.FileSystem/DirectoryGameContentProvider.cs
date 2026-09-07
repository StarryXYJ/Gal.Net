using GalNet.Core.Services;
using GalNet.Runtime.Loader;

namespace GalNet.Storage.FileSystem;

/// <summary>Loads game graph and groups from a published directory.</summary>
public sealed class DirectoryGameContentProvider : IGameContentProvider
{
    private readonly string _directory;

    public DirectoryGameContentProvider(string directory) => _directory = directory;

    public Task<GameContent> LoadAsync(CancellationToken cancellationToken = default)
    {
        var graph = GraphLoader.LoadFromFile(Path.Combine(_directory, "graph.json"));
        foreach (var group in graph.Nodes.OfType<GalNet.Core.Graph.Group>())
        {
            var path = Path.Combine(_directory, $"{group.Id}.galgroup");
            if (File.Exists(path)) GalgroupLoader.LoadIntoGroup(group, path);
        }

        return Task.FromResult(new GameContent { Graph = graph, AssetRoot = _directory });
    }
}
