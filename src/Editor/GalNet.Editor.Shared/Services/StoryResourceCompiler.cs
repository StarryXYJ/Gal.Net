using System.Text.Json;
using GalNet.Assets;
using GalNet.Core.Assets;
using GalNet.Core.Entry;
using GalNet.Core.Parameters;
using GalNet.Core.Serialization;

namespace GalNet.Editor.Shared.Services;

/// <summary>Adapts the compiled single-graph stage to ordinary addressable files.</summary>
public static class StoryResourceCompiler
{
    public static IReadOnlyList<IGameFile> Compile(string stage, IEntryCatalog catalog, CancellationToken cancellationToken = default) =>
        CompiledStoryResources.Create(File.ReadAllBytes(Path.Combine(stage, "Graph", "graph.json")), id =>
        {
            var path = GalNet.Assets.FileSystemPaths.ResolveRelativeFile(stage, "Graph/groups/" + id + ".galgroup");
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }, group => Dependencies(group, catalog), cancellationToken);

    public static AssetDependencyInfo Dependencies(CompiledGroupDocument group, IEntryCatalog catalog)
    {
        using var structures = new ValueStructureResolver();
        var references = new List<AssetDependency>();
        var complete = true;
        foreach (var entry in group.Entries)
        {
            if (!catalog.TryGet(entry.TypeId, out var definition)) { complete = false; continue; }
            foreach (var field in definition.DynamicParameters.Fields)
            {
                var wire = field.JsonPropertyName ?? field.Value.JsonOptions.PropertyNamingPolicy?.ConvertName(field.MemberName ?? field.Id) ?? field.Id;
                if (!entry.Arguments.TryGetProperty(wire, out var value)) continue;
                using var tree = new ValueDocument(field, value, structures);
                foreach (var property in tree.GetIterator(new(32, 100_000)))
                {
                    if (property.Value is not { } provided || IsEmpty(provided)) continue;
                    if (property.Issue is not null) { complete = false; continue; }
                    if (property.Field.ResourceTypeId is not { } type) continue;
                    if (provided.ValueKind != JsonValueKind.String) { complete = false; continue; }
                    references.Add(new(provided.GetString()!.Trim(), type == "*" ? null : type));
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
