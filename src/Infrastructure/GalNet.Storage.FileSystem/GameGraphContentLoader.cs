using GalNet.Core.Graph;
using GalNet.Runtime.Loader;

namespace GalNet.Storage.FileSystem;

internal static class GameGraphContentLoader
{
    public static Graph Load(string rootDirectory, string graphRelativePath, string groupsRelativeDirectory, CancellationToken cancellationToken)
    {
        var graph = GraphLoader.LoadFromFile(Path.Combine(rootDirectory, graphRelativePath));
        foreach (var group in graph.Nodes.OfType<Group>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(rootDirectory, groupsRelativeDirectory, $"{group.Id}.galgroup");
            if (File.Exists(path)) GalgroupLoader.LoadIntoGroup(group, path);
        }
        return graph;
    }
}
