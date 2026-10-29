using GalNet.Core.Assets;

namespace GalNet.Assets;

public sealed record PlannedPak(string PackageId, int PartIndex, int PartCount, IReadOnlyList<IGameFile> Files);
public sealed record ContentPackagePlan(IReadOnlyList<PlannedPak> Paks, IReadOnlyList<IGameFile> Unused);

/// <summary>Base closure first; optional story closures only subtract the base set, never one another.</summary>
public static class ContentPackagePlanner
{
    public static ContentPackagePlan Plan(IReadOnlyList<IGameFile> available, IEnumerable<string> extraRoots,
        IResourceTypeCatalog types, long maxBytes, IEnumerable<string>? declaredComplete = null, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBytes, 1);
        var graph = available.Single(file => file.Id == StoryResourceIds.Graph);
        var graphDependencies = ((IAssetDependencies)graph.Metadata).GetDependencies();
        var packages = graphDependencies.Direct.Where(reference => !string.IsNullOrEmpty(reference.RequiredStoryPackage))
            .GroupBy(reference => reference.RequiredStoryPackage!, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal).ToArray();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in packages)
            if (package.Key.Length > 128 || package.Key != package.Key.Trim() || package.Key.Any(char.IsControl) || !names.Add(package.Key))
                throw new InvalidDataException($"Invalid or case-ambiguous story package '{package.Key}'. Names must be trimmed and at most 128 characters.");
        var complete = (declaredComplete ?? []).ToArray();
        var baseline = Collect(extraRoots.Prepend(StoryResourceIds.Graph), null);
        var baseIds = baseline.Select(file => file.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (packages.SelectMany(package => package).Any(reference => baseIds.Contains(reference.AssetId)))
            throw new InvalidDataException("Base content cannot require an optional story group.");
        var result = new List<PlannedPak>();
        var used = new HashSet<string>(baseIds, StringComparer.OrdinalIgnoreCase);
        Split("", baseline);
        foreach (var package in packages)
        {
            var closure = Collect(package.Select(reference => reference.AssetId), package.Key);
            var otherGroups = packages.Where(other => other.Key != package.Key).SelectMany(other => other).Select(item => item.AssetId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (closure.Any(file => otherGroups.Contains(file.Id))) throw new InvalidDataException($"Story '{package.Key}' requires another optional story's group.");
            var own = closure.Where(file => !baseIds.Contains(file.Id)).ToArray();
            used.UnionWith(own.Select(file => file.Id));
            Split(package.Key, own);
        }
        return new(result, available.Where(file => !used.Contains(file.Id)).OrderBy(file => file.Id, StringComparer.Ordinal).ToArray());

        IReadOnlyList<IGameFile> Collect(IEnumerable<string> roots, string? package)
        {
            var closure = AssetDependencyCollector.Collect(available, roots, types, package, complete, ct);
            if (!closure.Complete) throw new InvalidDataException("Resource dependencies are incomplete: "
                + string.Join(", ", closure.IncompleteAssetIds) + ". Declare additional roots and explicitly confirm their coverage in galnet.packaging.");
            return closure.Files;
        }
        void Split(string package, IReadOnlyList<IGameFile> files)
        {
            var parts = new List<List<IGameFile>>();
            var current = new List<IGameFile>();
            long size = 0;
            foreach (var file in files.OrderBy(file => file.Id, StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                if (current.Count > 0 && file.Length > maxBytes - size)
                { parts.Add(current); current = []; size = 0; }
                current.Add(file);
                size = checked(size + file.Length);
            }
            if (current.Count != 0) parts.Add(current);
            for (var index = 0; index < parts.Count; index++) result.Add(new(package, index, parts.Count, parts[index]));
        }
    }

}
