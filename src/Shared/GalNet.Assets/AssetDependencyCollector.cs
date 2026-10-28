using GalNet.Core.Assets;

namespace GalNet.Assets;

public sealed record AssetDependencyClosure(IReadOnlyList<IGameFile> Files, IReadOnlyList<string> IncompleteAssetIds)
{
    public bool Complete => IncompleteAssetIds.Count == 0;
}

/// <summary>Computes references without choosing archive ownership or acquiring decoded assets.</summary>
public static class AssetDependencyCollector
{
    public static AssetDependencyClosure Collect(IEnumerable<IGameFile> available, IEnumerable<string> roots,
        IResourceTypeCatalog types, string? storyPackage = null, IEnumerable<string>? declaredComplete = null,
        CancellationToken cancellationToken = default)
    {
        var files = new Dictionary<string, IGameFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in available)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(file.Id) || !files.TryAdd(file.Id.Trim(), file))
                throw new InvalidDataException($"Duplicate or empty resource ID '{file.Id}'.");
        }
        var completeDeclarations = (declaredComplete ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = new Dictionary<string, IGameFile>(StringComparer.OrdinalIgnoreCase);
        var incomplete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<(string Id, string? Type, string Source)>(roots.Select(id => (id, (string?)null, "build root")));
        while (pending.TryDequeue(out var reference))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(reference.Id) || !files.TryGetValue(reference.Id.Trim(), out var file))
                throw new InvalidDataException($"Resource '{reference.Source}' references missing asset '{reference.Id}'.");
            if (reference.Type is not null && !StringComparer.Ordinal.Equals(file.TypeId, types.NormalizeTypeId(reference.Type)))
                throw new InvalidDataException($"Resource '{reference.Source}' expects '{reference.Id}' to have type '{reference.Type}', got '{file.TypeId}'.");
            if (!selected.TryAdd(file.Id.Trim(), file)) continue;
            var registration = types.Get(file.TypeId);
            var info = file.Metadata is IAssetDependencies dependencies
                ? dependencies.GetDependencies()
                : new AssetDependencyInfo([], registration.HasNoDependencies);
            if (!info.Complete && !completeDeclarations.Contains(file.Id)) incomplete.Add(file.Id);
            foreach (var dependency in info.Direct)
            {
                if (dependency is null) throw new InvalidDataException($"Resource '{file.Id}' has a null dependency.");
                if (dependency.RequiredStoryPackage is { Length: > 0 } required)
                {
                    if (file.TypeId != "graph" || dependency.ExpectedTypeId != "group")
                        throw new InvalidDataException("Only graph-to-group references may have optional story package references.");
                    if (!StringComparer.Ordinal.Equals(required, storyPackage)) continue;
                }
                pending.Enqueue((dependency.AssetId, dependency.ExpectedTypeId, file.Id));
            }
        }
        return new(selected.Values.OrderBy(file => file.Id, StringComparer.Ordinal).ToArray(), incomplete.Order(StringComparer.Ordinal).ToArray());
    }
}
