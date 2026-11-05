using System.Security.Cryptography;
using System.Text.Json;
using GalNet.Debugging;
using GalNet.Storage.FileSystem;

namespace GalNet.Editor.Shared.Services;

/// <summary>Publishes one immutable, self-describing content revision for a local debug application.</summary>
public static class DebugContentRevisionBuilder
{
    public static async Task<ProjectContentBuildResult> BuildAsync(
        ProjectContentBuilder builder,
        string projectRoot,
        string projectId,
        string contractFingerprint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(contractFingerprint);
        var root = Path.GetFullPath(projectRoot);
        var builds = Path.Combine(root, "Output", "Debug", "Builds");
        Directory.CreateDirectory(builds);
        var candidate = Path.Combine(builds, $".candidate-{Guid.NewGuid():N}");
        try
        {
            var build = await builder.BuildAsync(root, candidate, cancellationToken).ConfigureAwait(false);
            var contentFingerprint = DebugContentIdentity.Compute(candidate);
            var graphPath = Path.Combine(candidate, "Graph", "graph.json");
            var graphSha256 = HashFile(graphPath);
            var groups = build.SourceEntries.GroupBy(entry => entry.GroupId, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new DebugContentGroup(group.Key, group.OrderBy(entry => entry.Start).Select(entry =>
                    new DebugContentEntry(entry.EntryId, entry.Start, entry.End, entry.ContentSha256, entry.PrimitiveIds.ToArray())).ToArray()))
                .ToArray();
            var files = EnumerateSessionFiles(candidate).Select(path => new DebugContentFile(
                Path.GetRelativePath(candidate, path).Replace('\\', '/'), HashFile(path), SessionWide: true)).ToArray();
            var buildKey = ComputeBuildKey(contentFingerprint, contractFingerprint, build.AuthorSignature, groups);
            var manifest = new DebugContentManifest(DebugContentManifest.CurrentVersion, projectId,
                contentFingerprint, contractFingerprint, build.AuthorSignature, graphSha256, groups, files);
            await DebugContentManifestStore.WriteAsync(candidate, manifest, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var published = Path.Combine(builds, buildKey.ToLowerInvariant());
            if (Directory.Exists(published))
            {
                var existing = DebugContentManifestStore.Read(published);
                if (!string.Equals(existing.ContentFingerprint, manifest.ContentFingerprint, StringComparison.Ordinal) ||
                    !string.Equals(existing.ContractFingerprint, manifest.ContractFingerprint, StringComparison.Ordinal) ||
                    !string.Equals(existing.AuthorSignature, manifest.AuthorSignature, StringComparison.Ordinal))
                    throw new InvalidDataException($"Debug content build '{buildKey}' has conflicting metadata.");
                Directory.Delete(candidate, recursive: true);
                candidate = "";
            }
            else
            {
                await FilePublication.MoveDirectoryAsync(candidate, published, cancellationToken).ConfigureAwait(false);
                candidate = "";
            }
            return build with { OutputPath = published, Manifest = manifest };
        }
        finally
        {
            if (!string.IsNullOrEmpty(candidate) && Directory.Exists(candidate)) Directory.Delete(candidate, recursive: true);
        }
    }

    private static IEnumerable<string> EnumerateSessionFiles(string root)
    {
        foreach (var folder in new[] { "Assets", "Buildin", "I18n" })
        {
            var directory = Path.Combine(root, folder);
            if (Directory.Exists(directory))
                foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                    yield return file;
        }
        var settings = Path.Combine(root, "settings.json");
        if (File.Exists(settings)) yield return settings;
    }

    private static string ComputeBuildKey(string contentFingerprint, string contractFingerprint, string authorSignature,
        IReadOnlyList<DebugContentGroup> groups)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { contentFingerprint, contractFingerprint, authorSignature, groups });
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static string HashFile(string path)
    {
        using var input = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(input));
    }
}
