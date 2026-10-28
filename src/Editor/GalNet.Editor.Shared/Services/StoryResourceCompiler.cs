using System.Text.Json;
using GalNet.Assets;
using GalNet.Core.Assets;
using GalNet.Core.Entry;
using GalNet.Core.Parameters;
using GalNet.Core.Serialization;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Contracts.Builtins.Scene;
using System.Text;

namespace GalNet.Editor.Shared.Services;

/// <summary>Adapts the compiled single-graph stage to ordinary addressable files.</summary>
public static class StoryResourceCompiler
{
    public static IReadOnlyList<IGameFile> Compile(string stage, IEntryCatalog catalog, CancellationToken cancellationToken = default,
        IReadOnlyList<IGameFile>? assets = null)
    {
        var graphBytes = File.ReadAllBytes(Path.Combine(stage, "Graph", "graph.json"));
        var graph = JsonSerializer.Deserialize<EditorGraphDocument>(graphBytes, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } })!;
        byte[]? ReadGroup(string id)
        {
            var path = GalNet.Assets.FileSystemPaths.ResolveRelativeFile(stage, "Graph/groups/" + id + ".galgroup");
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        var document = new EditorProjectDocument { Graph = graph, GroupEntries = graph.Nodes
            .Where(node => node.Type is "Group" or "Entry").ToDictionary(node => node.Id, node => ReadGroup(node.Id) is { } bytes
                ? Authors(JsonCodec.Deserialize<CompiledGroupDocument>(bytes)!, catalog) : [], StringComparer.Ordinal) };
        var programs = new Dictionary<string, ShaderEffectDescriptor?>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in (assets ?? []).Where(file => file.TypeId == "effect-program"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (file.Length > 1024 * 1024) { programs[file.Id] = null; continue; }
            var parsed = ShaderEffectMetadataParser.Parse(new(file.Id), Encoding.UTF8.GetString(file.ReadAllBytes()));
            programs[file.Id] = parsed.Success ? parsed.Descriptor : null;
        }
        var metadata = new EntryParameterMetadata(Programs: programs);
        return CompiledStoryResources.Create(graphBytes, ReadGroup,
            (id, group) => Dependencies(group, catalog, document, metadata, id), cancellationToken);
    }

    public static AssetDependencyInfo Dependencies(CompiledGroupDocument group, IEntryCatalog catalog)
    {
        const string id = "main";
        var document = new EditorProjectDocument { Graph = new() { RootNodeId = id, Nodes = [new() { Id = id, Type = "Group" }] },
            GroupEntries = new() { [id] = Authors(group, catalog) } };
        return Dependencies(group, catalog, document, new(), id);
    }

    private static List<EditorEntryData> Authors(CompiledGroupDocument group, IEntryCatalog catalog) => group.Entries.Select(entry =>
    {
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        catalog.TryGet(entry.TypeId, out var definition);
        foreach (var value in entry.Arguments.EnumerateObject())
        {
            var field = definition?.DynamicParameters.Fields.FirstOrDefault(field => Wire(field) == value.Name);
            values[field?.Id ?? value.Name] = value.Value.Clone();
        }
        return new EditorEntryData { StableId = entry.Id, Type = entry.TypeId, Condition = entry.Condition, Parameters = values };
    }).ToList();

    private static string Wire(FieldDescriptor field) => field.JsonPropertyName
        ?? field.Value.JsonOptions.PropertyNamingPolicy?.ConvertName(field.MemberName ?? field.Id) ?? field.Id;

    private static AssetDependencyInfo Dependencies(CompiledGroupDocument group, IEntryCatalog catalog,
        EditorProjectDocument source, EntryParameterMetadata metadata, string groupId)
    {
        using var structures = new ValueStructureResolver();
        var provider = new EntryParameterStructures(source, metadata, catalog);
        var authors = source.GroupEntries[groupId].ToDictionary(entry => entry.StableId, StringComparer.Ordinal);
        var references = new List<AssetDependency>();
        var complete = true;
        foreach (var entry in group.Entries)
        {
            if (!catalog.TryGet(entry.TypeId, out var definition)) { complete = false; continue; }
            var author = authors[entry.Id];
            foreach (var field in definition.DynamicParameters.Fields)
            {
                var provided = entry.Arguments.TryGetProperty(Wire(field), out var value) ? value : field.DefaultValue;
                if (provided is null) continue;
                using var tree = new ValueDocument(provider.Describe(field, groupId, author), provided, structures, provider,
                    new EntryStructureContext(groupId, author));
                foreach (var property in tree.GetIterator(new(32, 100_000, IncludeMissing: true)))
                {
                    if ((property.Value ?? property.Field.DefaultValue) is not { } actual || IsEmpty(actual)) continue;
                    if (property.Issue is not null) { complete = false; continue; }
                    if (property.Field.ResourceTypeId is not { } type) continue;
                    if (actual.ValueKind != JsonValueKind.String) { complete = false; continue; }
                    references.Add(new(actual.GetString()!.Trim(), type == "*" ? null : type));
                }
            }
            // Calling arbitrary project code cannot establish a complete static resource set.
            if (entry.TypeId == "custom.invoke") complete = false;
        }
        return new(references.Distinct().OrderBy(reference => reference.AssetId, StringComparer.Ordinal).ToArray(), complete);
    }

    private static bool IsEmpty(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => true,
        JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString()),
        JsonValueKind.Object => !value.EnumerateObject().Any(),
        JsonValueKind.Array => value.GetArrayLength() == 0,
        _ => false
    };
}
