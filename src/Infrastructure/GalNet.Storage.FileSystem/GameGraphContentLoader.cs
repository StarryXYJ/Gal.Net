using GalNet.Core.Graph;
using GalNet.Runtime.Loader;
using GalNet.Core.Assets;
using GalNet.Core.Serialization;
using System.Text;
using System.Text.Json;

namespace GalNet.Storage.FileSystem;

internal static class GameGraphContentLoader
{
    public static async Task<Graph> LoadResourcesAsync(string rootDirectory, IAssetManager assets, CancellationToken cancellationToken)
    {
        if (!File.Exists(Path.Combine(rootDirectory, GameContentManifest.FileName)))
            throw new InvalidDataException("Installed content requires content.json and addressable story resources. Re-export the game.");
        var manifest = JsonCodec.Deserialize<GameContentManifest>(
            await File.ReadAllTextAsync(Path.Combine(rootDirectory, GameContentManifest.FileName), cancellationToken),
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { AllowDuplicateProperties = false,
                UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow })
            ?? throw new InvalidDataException("Missing game content descriptor.");
        if (manifest.Version != 1 || manifest.GraphId != StoryResourceIds.Graph)
            throw new InvalidDataException("Unsupported single-graph content descriptor.");
        return await LoadResourcesAsync(assets, manifest.GraphId, cancellationToken);
    }

    public static async Task<Graph> LoadResourcesAsync(IAssetManager assets, string graphId, CancellationToken cancellationToken)
    {
        var graphFile = await assets.GetFileAsync(graphId, cancellationToken);
        if (graphFile?.TypeId != "graph") throw new InvalidDataException("Required graph resource is missing or has the wrong type.");
        var graph = GraphLoader.LoadFromJson(Encoding.UTF8.GetString(await graphFile.ReadAllBytesAsync(cancellationToken)));
        if (graph.Id != graphFile.Id) throw new InvalidDataException("Graph resource identity does not match its payload.");
        foreach (var group in graph.Nodes.OfType<Group>())
        {
            var file = await assets.GetFileAsync(group.ResourceId, cancellationToken);
            if (file?.TypeId != "group") throw new InvalidDataException($"Required group resource '{group.ResourceId}' is missing or has the wrong type.");
            GalgroupLoader.LoadIntoGroupFromContent(group, Encoding.UTF8.GetString(await file.ReadAllBytesAsync(cancellationToken)));
        }
        return graph;
    }

}
