using GalNet.Core.Graph;
using GalNet.Runtime.Loader;
using GalNet.Core.Assets;
using GalNet.Core.Serialization;
using System.Text;

namespace GalNet.Storage.FileSystem;

internal static class GameGraphContentLoader
{
    public static async Task<Graph> LoadResourcesAsync(IAssetManager assets, string graphId, CancellationToken cancellationToken,
        IReadOnlySet<string>? installedStories = null, IReadOnlySet<string>? declaredStories = null)
    {
        var graphFile = await assets.GetFileAsync(graphId, cancellationToken);
        if (graphFile?.TypeId != "graph") throw new InvalidDataException("Required graph resource is missing or has the wrong type.");
        var graph = GraphLoader.LoadFromJson(Encoding.UTF8.GetString(await graphFile.ReadAllBytesAsync(cancellationToken)));
        if (graph.Id != graphFile.Id) throw new InvalidDataException("Graph resource identity does not match its payload.");
        foreach (var group in graph.Nodes.OfType<Group>())
        {
            if (group.StoryPackage.Length > 0 && installedStories is not null)
            {
                if (declaredStories?.Contains(group.StoryPackage) != true) throw new InvalidDataException($"Graph references undeclared story package '{group.StoryPackage}'.");
                if (!installedStories.Contains(group.StoryPackage)) { group.IsAvailable = false; continue; }
            }
            var file = await assets.GetFileAsync(group.ResourceId, cancellationToken);
            if (file?.TypeId != "group") throw new InvalidDataException($"Required group resource '{group.ResourceId}' is missing or has the wrong type.");
            GalgroupLoader.LoadIntoGroupFromContent(group, Encoding.UTF8.GetString(await file.ReadAllBytesAsync(cancellationToken)));
        }
        var groups = graph.Nodes.OfType<Group>().ToDictionary(group => group.Id, StringComparer.Ordinal);
        foreach (var group in groups.Values.Where(group => !group.IsAvailable))
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var current = group;
            while (current.Entries.Count == 0)
            {
                if (!visited.Add(current.Id)) throw new InvalidDataException("Skipping absent story packages creates an empty-group cycle.");
                var next = graph.Edges.FirstOrDefault(edge => edge.FromNodeId == current.Id && edge.FromOutlet == 0)?.ToNodeId;
                if (next is null || !groups.TryGetValue(next, out current!)) break;
            }
        }
        return graph;
    }

}
