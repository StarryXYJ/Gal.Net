using System.Text.Json;
using System.Text.Json.Nodes;
using GalNet.Core.Assets;
using GalNet.Core.Serialization;

namespace GalNet.Assets;

/// <summary>Owns the effective story bytes shared by development providers and publication.</summary>
public static class CompiledStoryResources
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<IGameFile> Create(byte[] graphBytes, Func<string, byte[]?> readGroup,
        Func<string, CompiledGroupDocument, AssetDependencyInfo>? analyzeGroup = null, CancellationToken cancellationToken = default)
    {
        var graph = JsonNode.Parse(graphBytes)?.AsObject() ?? throw new InvalidDataException("Missing compiled graph.");
        graph["id"] = StoryResourceIds.Graph;
        var files = new List<IGameFile>();
        var references = new List<AssetDependency>();
        if (graph["nodes"] is not JsonArray nodes) throw new InvalidDataException("Compiled graph requires a node array.");
        foreach (var node in nodes.OfType<JsonObject>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node["type"]?.GetValue<string>() is not ("Group" or "Entry")) continue;
            var id = node["id"]!.GetValue<string>();
            var resourceId = StoryResourceIds.Group(id);
            var package = node["storyPackage"]?.GetValue<string>() ?? "";
            node["resourceId"] = resourceId;
            var bytes = readGroup(id)
                ?? (node["type"]!.GetValue<string>() == "Entry" ? JsonCodec.SerializeToUtf8Bytes(new CompiledGroupDocument(), Options)
                    : throw new InvalidDataException($"Compiled group '{id}' is missing. Build the project first."));
            var document = JsonCodec.Deserialize<CompiledGroupDocument>(bytes, Options)
                ?? throw new InvalidDataException($"Compiled group '{id}' is invalid.");
            if (document.Kind != GroupDocumentKind.Compiled || document.Version != CompiledGroupDocument.CurrentVersion)
                throw new InvalidDataException($"Group '{id}' is not compiled for this runtime.");
            var dependencies = analyzeGroup?.Invoke(id, document) ?? new AssetDependencyInfo([], document.Entries.Count == 0);
            var meta = new DependentAssetMeta { Id = resourceId, TypeId = "group",
                Path = "Story/groups/" + Uri.EscapeDataString(id) + ".galgroup",
                Dependencies = dependencies.Direct.ToList(), DependenciesComplete = dependencies.Complete };
            files.Add(new GameFile(meta.Id, meta.Path, meta.TypeId, meta, bytes));
            references.Add(new(resourceId, "group", package.Length == 0 ? null : package));
        }
        var graphMeta = new DependentAssetMeta { Id = StoryResourceIds.Graph, TypeId = "graph", Path = "Story/main.galgraph",
            DependenciesComplete = true, Dependencies = references };
        files.Add(new GameFile(graphMeta.Id, graphMeta.Path, graphMeta.TypeId, graphMeta, JsonCodec.SerializeToUtf8Bytes(graph, Options)));
        return files;
    }
}
