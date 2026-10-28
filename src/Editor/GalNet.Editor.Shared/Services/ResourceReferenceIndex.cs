using System.Text.Json;
using GalNet.Core.Assets;
using GalNet.Core.Configuration;
using GalNet.Core.Entry;
using GalNet.Core.Parameters;
using GalNet.Editor.Abstraction.Documents;

namespace GalNet.Editor.Shared.Services;

public sealed record ResourceUse(string GroupId, string EntryId, string FieldPath, string? Through = null);
public sealed record ResourceReferenceRow(string Id, string Path, string TypeId, bool Missing, IReadOnlyList<ResourceUse> Uses);

/// <summary>Author snapshot resource references across every Group, plus declared/indirect resources.</summary>
public sealed class ResourceReferenceIndex
{
    public IReadOnlyList<ResourceReferenceRow> Rows { get; }
    public IReadOnlyList<string> Issues { get; }
    public ResourceReferenceIndex(EditorProjectDocument document, IEntryCatalog catalog, IReadOnlyList<IGameFile> files, EntryParameterMetadata metadata)
    {
        var resources = files.ToDictionary(file => file.Id, StringComparer.OrdinalIgnoreCase);
        var references = new Dictionary<string, (string? Type, List<ResourceUse> Uses)>(StringComparer.OrdinalIgnoreCase);
        var issues = new HashSet<string>(StringComparer.Ordinal);
        var provider = new EntryParameterStructures(document, metadata, catalog);
        using var structures = new ValueStructureResolver();
        foreach (var (groupId, entries) in document.GroupEntries)
            foreach (var entry in entries)
            {
                if (!catalog.TryGet(entry.Type, out var definition)) { issues.Add(groupId + "/" + entry.StableId + ": schema unavailable"); continue; }
                foreach (var field in definition.DynamicParameters.Fields)
                {
                    var value = entry.Parameters.GetValueOrDefault(field.Id);
                    var effective = value.ValueKind == JsonValueKind.Undefined ? field.DefaultValue : value;
                    if (effective is null) continue;
                    using var tree = new ValueDocument(provider.Describe(field, groupId, entry), effective, structures, provider, new EntryStructureContext(groupId, entry));
                    foreach (var property in tree.GetIterator(new(32, 100_000, IncludeMissing: true)))
                    {
                        var actual = property.Value ?? property.Field.DefaultValue;
                        if (actual is null || Empty(actual.Value)) continue;
                        if (property.Issue is not null) { issues.Add(groupId + "/" + entry.StableId + "/" + field.Id + ": " + property.Issue.Message); continue; }
                        if (property.Field.ResourceTypeId is not { } type || actual.Value.ValueKind != JsonValueKind.String) continue;
                        Add(actual.Value.GetString()!, type == "*" ? null : type, new(groupId, entry.StableId, "/" + field.Id + property.Path.ToLogicalPointer()), []);
                    }
                }
            }
        var overrides = document.Configuration.Read(ResourceOverrideOptions.Section);
        foreach (var mapping in overrides.Packages.SelectMany(package => package.Mappings))
            if (!resources.ContainsKey(mapping.Value)) issues.Add("Missing replacement resource: " + mapping.Value);
        foreach (var id in document.Configuration.Read(ContentPackagingOptions.Section).IncludeResources
                     .Concat(overrides.AdditionalOriginals).Concat(overrides.Packages.SelectMany(package => package.Mappings.Keys)))
            Add(id, null, new("", "", "explicit"), []);
        Rows = references.OrderBy(pair => resources.GetValueOrDefault(pair.Key)?.Path ?? pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new ResourceReferenceRow(pair.Key, resources.GetValueOrDefault(pair.Key)?.Path ?? pair.Key,
                resources.GetValueOrDefault(pair.Key)?.TypeId ?? pair.Value.Type ?? "", !resources.ContainsKey(pair.Key), pair.Value.Uses.Distinct().ToArray())).ToArray();
        Issues = issues.Order(StringComparer.Ordinal).ToArray();

        void Add(string id, string? type, ResourceUse use, HashSet<string> visited)
        {
            id = id.Trim(); if (id.Length == 0 || !visited.Add(id)) return;
            var file = resources.GetValueOrDefault(id);
            if (file?.TypeId is "graph" or "group") return;
            if (!references.TryGetValue(id, out var row)) references.Add(id, row = (type, []));
            row.Uses.Add(use);
            if (file is null) { issues.Add("Missing resource: " + id); return; }
            if (type is not null && file.TypeId != type) issues.Add("Resource type mismatch: " + id);
            if (file.Metadata is not IAssetDependencies dependencies) return;
            var information = dependencies.GetDependencies();
            if (!information.Complete) issues.Add("Incomplete resource dependencies: " + id);
            foreach (var dependency in information.Direct)
                Add(dependency.AssetId, dependency.ExpectedTypeId, use with { Through = id }, new(visited, StringComparer.OrdinalIgnoreCase));
        }
    }
    private static bool Empty(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => true,
        JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString()),
        JsonValueKind.Object => !value.EnumerateObject().Any(),
        JsonValueKind.Array => value.GetArrayLength() == 0,
        _ => false
    };
}
