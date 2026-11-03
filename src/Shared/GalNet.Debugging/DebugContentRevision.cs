using System.Text.Json;

namespace GalNet.Debugging;

public enum DebugContentImpact
{
    Direct,
    ReturnGroupStart,
    RestartGame
}

public sealed record DebugContentEntry(
    string Id,
    int Start,
    int End,
    string ContentSha256,
    string[] PrimitiveIds);

public sealed record DebugContentGroup(string Id, DebugContentEntry[] Entries);

public sealed record DebugContentFile(string Path, string Sha256, bool SessionWide = false);

public sealed record DebugContentManifest(
    int Version,
    string ProjectId,
    string ContentFingerprint,
    string ContractFingerprint,
    string AuthorSignature,
    string GraphSha256,
    DebugContentGroup[] Groups,
    DebugContentFile[] Files)
{
    public const int CurrentVersion = 1;
    public const string RelativePath = ".galnet/debug-content.json";
}

public static class DebugContentManifestStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task WriteAsync(string root, DebugContentManifest manifest, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(manifest);
        var path = Path.Combine(Path.GetFullPath(root), DebugContentManifest.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(manifest, Json), cancellationToken).ConfigureAwait(false);
    }

    public static DebugContentManifest Read(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var path = Path.Combine(Path.GetFullPath(root), DebugContentManifest.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        return JsonSerializer.Deserialize<DebugContentManifest>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException("Debug content manifest is empty.");
    }
}

public sealed record DebugContentPosition(
    string? CurrentGroupId,
    int EntryIndex,
    string[] ActivePrimitiveIds,
    string[] VisitedGroups);

/// <summary>Pure comparison of two verified debug manifests against one captured execution boundary.</summary>
public static class DebugContentImpactClassifier
{
    public static int MapCandidateEntryIndex(
        DebugContentManifest current,
        DebugContentManifest candidate,
        DebugContentPosition position)
    {
        Validate(current);
        Validate(candidate);
        var groupId = position.CurrentGroupId ?? throw new InvalidOperationException("There is no current Group to map.");
        var before = current.Groups.SingleOrDefault(group => string.Equals(group.Id, groupId, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"Current debug content does not contain Group '{groupId}'.");
        var after = candidate.Groups.SingleOrDefault(group => string.Equals(group.Id, groupId, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"Candidate debug content does not contain Group '{groupId}'.");
        var prefixCount = ExecutedPrefixCount(before, position);
        if (!KeepsExecutedPrefix(before, after, position))
            throw new InvalidDataException("Candidate content does not preserve the executed author boundary.");
        return prefixCount == 0 ? 0 : after.Entries[prefixCount - 1].End;
    }

    public static int ExecutedAuthorEntryCount(DebugContentManifest content, DebugContentPosition position)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(position);
        Validate(content);
        var groupId = position.CurrentGroupId ?? throw new InvalidOperationException("There is no current Group.");
        var group = content.Groups.SingleOrDefault(item => string.Equals(item.Id, groupId, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"Debug content does not contain Group '{groupId}'.");
        return ExecutedPrefixCount(group, position);
    }

    public static DebugContentImpact Classify(
        DebugContentManifest current,
        DebugContentManifest candidate,
        DebugContentPosition position)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(position);
        Validate(current);
        Validate(candidate);
        if (!string.Equals(current.ProjectId, candidate.ProjectId, StringComparison.Ordinal))
            throw new InvalidDataException("Debug content belongs to a different project.");
        if (!string.Equals(current.ContractFingerprint, candidate.ContractFingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("Project contracts changed; restart the debug application.");
        if (string.Equals(current.ContentFingerprint, candidate.ContentFingerprint, StringComparison.Ordinal) &&
            string.Equals(current.AuthorSignature, candidate.AuthorSignature, StringComparison.Ordinal))
            return DebugContentImpact.Direct;

        var impact = DebugContentImpact.Direct;
        void Raise(DebugContentImpact value) => impact = value > impact ? value : impact;

        if (!string.Equals(current.GraphSha256, candidate.GraphSha256, StringComparison.Ordinal))
            Raise(DebugContentImpact.RestartGame);

        var oldGroups = current.Groups.ToDictionary(group => group.Id, StringComparer.Ordinal);
        var newGroups = candidate.Groups.ToDictionary(group => group.Id, StringComparer.Ordinal);
        var visited = position.VisitedGroups.ToHashSet(StringComparer.Ordinal);
        foreach (var id in oldGroups.Keys.Concat(newGroups.Keys).Distinct(StringComparer.Ordinal))
        {
            oldGroups.TryGetValue(id, out var before);
            newGroups.TryGetValue(id, out var after);
            if (Equivalent(before, after)) continue;

            if (string.Equals(id, position.CurrentGroupId, StringComparison.Ordinal))
            {
                if (before is null || after is null)
                {
                    Raise(DebugContentImpact.RestartGame);
                    continue;
                }
                if (!KeepsExecutedPrefix(before, after, position)) Raise(DebugContentImpact.ReturnGroupStart);
            }
            else if (visited.Contains(id))
                Raise(DebugContentImpact.RestartGame);
        }

        var oldFiles = current.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        var newFiles = candidate.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var path in oldFiles.Keys.Concat(newFiles.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            oldFiles.TryGetValue(path, out var before);
            newFiles.TryGetValue(path, out var after);
            if (before is not null && after is not null &&
                string.Equals(before.Sha256, after.Sha256, StringComparison.Ordinal)) continue;
            Raise(DebugContentImpact.RestartGame);
        }

        return impact;
    }

    private static bool KeepsExecutedPrefix(DebugContentGroup before, DebugContentGroup after, DebugContentPosition position)
    {
        var prefixCount = ExecutedPrefixCount(before, position);
        if (after.Entries.Length < prefixCount) return false;
        for (var index = 0; index < prefixCount; index++)
        {
            var oldEntry = before.Entries[index];
            var newEntry = after.Entries[index];
            if (!string.Equals(oldEntry.Id, newEntry.Id, StringComparison.Ordinal) ||
                !string.Equals(oldEntry.ContentSha256, newEntry.ContentSha256, StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    private static int ExecutedPrefixCount(DebugContentGroup group, DebugContentPosition position)
    {
        var active = position.ActivePrimitiveIds.ToHashSet(StringComparer.Ordinal);
        var activeIndex = Array.FindLastIndex(group.Entries,
            entry => entry.PrimitiveIds.Any(active.Contains));
        if (activeIndex >= 0) return activeIndex + 1;

        var count = 0;
        foreach (var entry in group.Entries)
        {
            if (entry.End <= position.EntryIndex) count++;
            else if (entry.Start < position.EntryIndex) { count++; break; }
            else break;
        }
        return count;
    }

    private static bool Equivalent(DebugContentGroup? first, DebugContentGroup? second)
    {
        if (ReferenceEquals(first, second)) return true;
        if (first is null || second is null || first.Entries.Length != second.Entries.Length) return false;
        for (var index = 0; index < first.Entries.Length; index++)
        {
            var left = first.Entries[index];
            var right = second.Entries[index];
            if (!string.Equals(left.Id, right.Id, StringComparison.Ordinal) ||
                !string.Equals(left.ContentSha256, right.ContentSha256, StringComparison.Ordinal) ||
                left.Start != right.Start || left.End != right.End ||
                !left.PrimitiveIds.SequenceEqual(right.PrimitiveIds, StringComparer.Ordinal)) return false;
        }
        return true;
    }

    private static void Validate(DebugContentManifest manifest)
    {
        if (manifest.Version != DebugContentManifest.CurrentVersion)
            throw new InvalidDataException($"Unsupported debug content manifest version {manifest.Version}.");
        if (string.IsNullOrWhiteSpace(manifest.ProjectId))
            throw new InvalidDataException("Debug content manifest identity is incomplete.");
        if (manifest.Groups.GroupBy(group => group.Id, StringComparer.Ordinal).Any(group => group.Count() != 1))
            throw new InvalidDataException("Debug content manifest contains duplicate Group identities.");
        foreach (var group in manifest.Groups)
        {
            if (group.Entries.GroupBy(entry => entry.Id, StringComparer.Ordinal).Any(entry => entry.Count() != 1))
                throw new InvalidDataException($"Debug content Group '{group.Id}' contains duplicate author Entry identities.");
            if (group.Entries.Any(entry => entry.Start < 0 || entry.End < entry.Start))
                throw new InvalidDataException($"Debug content Group '{group.Id}' contains an invalid author span.");
        }
    }
}
