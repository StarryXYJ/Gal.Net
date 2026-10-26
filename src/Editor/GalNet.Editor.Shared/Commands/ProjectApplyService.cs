using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Core.Assets;
using GalNet.Core.Entry;
using GalNet.Editor.Abstraction.Changes;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Shared.Commands;

/// <summary>Optimistic, all-or-compensated publication of externally prepared project files.</summary>
public sealed class ProjectApplyService(string root, IEntryCatalog catalog, AuthorFileTransaction? transaction = null,
    GalNet.Core.Configuration.ProjectConfigurationSchema? configurations = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, PropertyNameCaseInsensitive = false, RespectNullableAnnotations = true };

    public async Task<ToolResult> ApplyAsync(string manifestFile, bool dryRun = false, CancellationToken cancellationToken = default)
    {
        const string operation = "project.apply";
        string? scratch = null;
        try
        {
            var reader = new ProjectRawReader(root);
            manifestFile = Path.GetFullPath(manifestFile);
            var sources = new ProjectRawReader(Path.GetDirectoryName(manifestFile)!);
            using var raw = JsonDocument.Parse(await File.ReadAllBytesAsync(sources.Resolve(Path.GetFileName(manifestFile)), cancellationToken));
            var duplicates = new List<ToolDiagnostic>();
            ProjectValidationService.DuplicateProperties(raw.RootElement, "apply-manifest", "", duplicates);
            if (duplicates.Count != 0) return new(operation, false, null, duplicates);
            var manifest = raw.RootElement.Deserialize<ProjectApplyManifest>(JsonOptions) ?? throw new JsonException("An apply manifest must be an object.");
            if (manifest.Version != 1 || manifest.Writes is null || manifest.Deletes is null || manifest.Writes.Count + manifest.Deletes.Count == 0)
                throw new ArgumentException("An apply manifest requires version 1 and at least one write or deletion.");
            var targets = new HashSet<string>(ProjectFilePaths.Comparer);
            var plan = new ProjectFilePlan();
            foreach (var write in manifest.Writes)
            {
                ArgumentNullException.ThrowIfNull(write);
                var path = Target(write.Path, deleting: false);
                if (!targets.Add(path)) throw new ArgumentException($"Duplicate apply target '{path}'.");
                var expected = HashValue(write.ExpectedHash, allowNull: true);
                if (HashFile(reader.Resolve(path)) != expected) throw new IOException($"Apply target changed: '{path}'.");
                plan.ExpectedHashes.Add(path, expected);
                _ = HashValue(write.ContentHash, allowNull: false);
                _ = sources.Resolve(ProjectFilePaths.Normalize(write.Source));
            }
            foreach (var deletion in manifest.Deletes)
            {
                ArgumentNullException.ThrowIfNull(deletion);
                var path = Target(deletion.Path, deleting: true);
                if (!targets.Add(path)) throw new ArgumentException($"Duplicate apply target '{path}'.");
                var expected = HashValue(deletion.ExpectedHash, allowNull: false);
                if (HashFile(reader.Resolve(path)) != expected) throw new IOException($"Apply target changed: '{path}'.");
                plan.ExpectedHashes.Add(path, expected); plan.Deletes.Add(path);
            }
            if (ExternalProjectSnapshotReader.HasPendingTransaction(root)) throw new IOException("Project publication is already in progress.");
            scratch = Path.Combine(Path.GetTempPath(), "GalNet-apply-" + Guid.NewGuid().ToString("N"));
            var payloads = Path.Combine(scratch, "payloads"); var candidate = Path.Combine(scratch, "project");
            Directory.CreateDirectory(payloads); Directory.CreateDirectory(candidate);
            var contentHashes = new Dictionary<string, string>(ProjectFilePaths.Comparer);
            for (var index = 0; index < manifest.Writes.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var write = manifest.Writes[index]; var path = Target(write.Path, false);
                var payload = Path.Combine(payloads, index.ToString(System.Globalization.CultureInfo.InvariantCulture));
                File.Copy(sources.Resolve(ProjectFilePaths.Normalize(write.Source)), payload);
                var hash = HashFile(payload)!;
                if (hash != HashValue(write.ContentHash, false)) throw new IOException($"Apply source changed or has an incorrect hash: '{write.Source}'.");
                plan.Copies.Add(path, payload); contentHashes.Add(path, hash);
            }
            var diagnostics = new List<ToolDiagnostic>();
            var originalFiles = reader.Files(null, diagnostics);
            if (diagnostics.Count != 0) return new(operation, false, null, diagnostics);
            var finalFiles = originalFiles.ToDictionary(path => path, reader.Resolve, ProjectFilePaths.Comparer);
            foreach (var path in plan.Deletes) finalFiles.Remove(path);
            foreach (var pair in plan.Copies) finalFiles[pair.Key] = pair.Value;
            CheckAssets(reader, finalFiles, targets);
            var types = BuiltinResourceTypes.CreateCatalog(); var codec = new AssetMetaCodec(types);
            var descriptors = finalFiles.Where(pair => pair.Key.StartsWith("Assets/", StringComparison.Ordinal) && pair.Key.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                .Where(pair => codec.Deserialize(File.ReadAllText(pair.Value), pair.Key[7..^5]).TypeId == "image-variants")
                .Select(pair => pair.Key[..^5]).ToHashSet(ProjectFilePaths.Comparer);
            // Validation checks author JSON, sidecars, descriptor content and resource existence.
            // Existing media payloads are intentionally not copied or decoded, matching validate's skipped asset-payloads check.
            foreach (var (path, source) in finalFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = Path.Combine(candidate, path); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var needsContent = path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".rawgalgroup", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) || descriptors.Contains(path);
                if (needsContent)
                {
                    File.Copy(source, target);
                    if (!plan.Copies.ContainsKey(path)) plan.ExpectedHashes.TryAdd(path, HashFile(target));
                }
                else using (File.Create(target)) { }
            }
            // Empty descriptor folders participate in image-variants validation too.
            CopyEmptyAssetDirectories(reader, candidate);
            _ = DirectProjectPersistence.LoadSettings(candidate);
            var validation = new ProjectValidationService(candidate).Validate(catalog: catalog, configurations: configurations);
            var referenceDiagnostics = validation.Success ? RemovedReferences(reader, candidate, finalFiles, targets, plan.Deletes) : [];
            var report = new ProjectApplyReport(dryRun, plan.Copies.Keys.Order(StringComparer.Ordinal).ToArray(), plan.Deletes.Order(StringComparer.Ordinal).ToArray(),
                contentHashes, (ProjectValidationReport)validation.Data!);
            var allDiagnostics = validation.Diagnostics.Concat(referenceDiagnostics).ToArray();
            if (!validation.Success || referenceDiagnostics.Any(item => item.Severity == "error")) return new(operation, false, report, allDiagnostics);
            if (!dryRun)
            {
                var nowDiagnostics = new List<ToolDiagnostic>(); var currentFiles = reader.Files(null, nowDiagnostics);
                if (nowDiagnostics.Count != 0 || !originalFiles.ToHashSet(ProjectFilePaths.Comparer).SetEquals(currentFiles))
                    throw new IOException("The project file set changed during apply preparation.");
                (transaction ?? new AuthorFileTransaction()).Commit(root, new ProjectSavePlan(new Dictionary<string, byte[]>(), []) { Files = plan }, cancellationToken);
            }
            return new(operation, true, report, allDiagnostics);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or JsonException or InvalidOperationException or KeyNotFoundException)
        { return new(operation, false, null, [new("project.applyRejected", "error", error.Message)]); }
        finally
        {
            if (scratch is not null && Directory.Exists(scratch))
                try { Directory.Delete(scratch, true); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Serilog.Log.Warning(error, "Apply staging cleanup deferred: {Directory}", scratch); }
        }
    }

    private static string Target(string path, bool deleting)
    {
        path = ProjectFilePaths.Normalize(path);
        if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) path = "Assets/" + path[7..];
        if (path is "Graph/graph.json" or "settings.json")
        { if (deleting) throw new ArgumentException($"Cannot delete project entry point '{path}'."); return path; }
        if (path == LocalizationJson.CatalogPath || path == LocalizationJson.MetadataPath) return path;
        if (path.StartsWith("Assets/", StringComparison.Ordinal)) return path;
        return ProjectFilePaths.Editable(path);
    }

    private static void CheckAssets(ProjectRawReader reader, IReadOnlyDictionary<string, string> finalFiles, IEnumerable<string> targets)
    {
        foreach (var path in targets.Where(path => path.StartsWith("Assets/", StringComparison.Ordinal)))
        {
            var resource = path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) ? path[..^5] : path;
            if (finalFiles.ContainsKey(resource) != finalFiles.ContainsKey(resource + ".meta"))
                throw new InvalidDataException($"Resource and metadata must exist or be deleted together: '{resource}'.");
            if (!path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) || !finalFiles.TryGetValue(path, out var source) || !File.Exists(reader.Resolve(path))) continue;
            using var before = JsonDocument.Parse(File.ReadAllBytes(reader.Resolve(path)));
            using var after = JsonDocument.Parse(File.ReadAllBytes(source));
            if (ProjectRawReader.MetadataIdentity(before.RootElement).Id != ProjectRawReader.MetadataIdentity(after.RootElement).Id)
                throw new InvalidDataException($"Replacing existing metadata must preserve its resource identity: '{path}'.");
        }
    }

    private static void CopyEmptyAssetDirectories(ProjectRawReader reader, string candidate)
    {
        if (!Directory.Exists(reader.Resolve("Assets"))) return;
        var pending = new Stack<string>(); pending.Push("Assets");
        while (pending.TryPop(out var path))
        {
            Directory.CreateDirectory(Path.Combine(candidate, path));
            foreach (var directory in Directory.EnumerateDirectories(reader.Resolve(path)))
            { var relative = reader.Relative(directory); _ = reader.Resolve(relative); pending.Push(relative); }
        }
    }

    private static IReadOnlyList<ToolDiagnostic> RemovedReferences(ProjectRawReader reader, string candidate, IReadOnlyDictionary<string, string> finalFiles,
        IEnumerable<string> targets, IEnumerable<string> deleted)
    {
        var removed = deleted.Select(path => (Value: path, Kind: "resource")).ToList();
        var remainingIds = finalFiles.Where(pair => pair.Key.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)).Select(pair =>
        { using var json = JsonDocument.Parse(File.ReadAllBytes(pair.Value)); return ProjectRawReader.MetadataIdentity(json.RootElement).Id; }).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in targets.Where(path => path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) && File.Exists(reader.Resolve(path))))
        {
            using var json = JsonDocument.Parse(File.ReadAllBytes(reader.Resolve(path)));
            if (ProjectRawReader.MetadataIdentity(json.RootElement).Id is { } id && !remainingIds.Contains(id)) removed.Add((id, "resource"));
        }
        using var oldGraph = JsonDocument.Parse(File.ReadAllBytes(reader.Resolve("Graph/graph.json")));
        using var nextGraph = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(candidate, "Graph/graph.json")));
        var nextIds = nextGraph.RootElement.GetProperty("nodes").EnumerateArray().Select(node => ProjectRawReader.Text(node, "id")).ToHashSet(StringComparer.Ordinal);
        foreach (var node in oldGraph.RootElement.GetProperty("nodes").EnumerateArray())
            if (ProjectRawReader.Text(node, "id") is { } id && !nextIds.Contains(id)) removed.Add((id, ProjectRawReader.Text(node, "type") == "Group" ? "group" : "node"));
        var result = new List<ToolDiagnostic>();
        var query = new ProjectQueryService(candidate);
        foreach (var (value, kind) in removed.Distinct())
        {
            var scan = query.References(value, kind); result.AddRange(scan.Diagnostics);
            foreach (var reference in ((QueryPage<ProjectReferenceItem>)scan.Data!).Items)
                result.Add(new("project.applyReference", "error", $"A static JSON reference to removed content '{value}' remains. Update it in the same batch.", reference.File, JsonPointer: reference.JsonPointer));
        }
        return result;
    }

    private static string? HashValue(string? value, bool allowNull)
    {
        if (value is null && allowNull) return null;
        if (value is null || value.Length != 64 || !value.All(Uri.IsHexDigit)) throw new ArgumentException("Hashes must be 64 hexadecimal SHA-256 characters; expectedHash=null explicitly requests a new file.");
        return value.ToUpperInvariant();
    }
    private static string? HashFile(string path)
    {
        if (!File.Exists(path)) return null;
        using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream));
    }
}
