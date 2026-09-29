using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Core.Compilation;
using GalNet.Core.Entry;
using GalNet.Core.Serialization;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Primitives.Builtins;

namespace GalNet.Editor.Shared.Services;

/// <summary>Builds editable project content into the compiled directory layout consumed by Runtime.</summary>
public sealed class ProjectContentBuilder(IEntryCatalog? entryCatalog = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IEntryCatalog _entryCatalog = entryCatalog ?? BuiltinEntryModules.CreateRecommendedTargetProfile();

    public async Task<ProjectContentBuildResult> BuildAsync(
        string projectRoot,
        string? outputRoot = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        var sourceRoot = Path.GetFullPath(projectRoot);
        if (!Directory.Exists(sourceRoot))
            throw new DirectoryNotFoundException($"Project directory not found: {sourceRoot}");

        var targetRoot = Path.GetFullPath(outputRoot ?? Path.Combine(sourceRoot, "Output"));
        ValidateOutputPath(sourceRoot, targetRoot);

        var stageRoot = Path.Combine(
            Path.GetDirectoryName(targetRoot) ?? throw new InvalidOperationException("Build output must have a parent directory."),
            $".{Path.GetFileName(targetRoot)}.build-{Guid.NewGuid():N}");
        string? backupRoot = null;
        try
        {
            Directory.CreateDirectory(stageRoot);
            var compiledGroups = await BuildStageAsync(sourceRoot, stageRoot, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            Directory.CreateDirectory(Path.GetDirectoryName(targetRoot)!);
            if (Directory.Exists(targetRoot))
            {
                backupRoot = targetRoot + $".previous-{Guid.NewGuid():N}";
                Directory.Move(targetRoot, backupRoot);
            }

            try
            {
                Directory.Move(stageRoot, targetRoot);
                stageRoot = "";
            }
            catch
            {
                if (backupRoot is not null && !Directory.Exists(targetRoot))
                {
                    Directory.Move(backupRoot, targetRoot);
                    backupRoot = null;
                }
                throw;
            }

            if (backupRoot is not null)
            {
                Directory.Delete(backupRoot, recursive: true);
                backupRoot = null;
            }

            return new ProjectContentBuildResult(targetRoot, compiledGroups);
        }
        finally
        {
            if (!string.IsNullOrEmpty(stageRoot) && Directory.Exists(stageRoot))
                Directory.Delete(stageRoot, recursive: true);
            if (backupRoot is not null && Directory.Exists(backupRoot) && !Directory.Exists(targetRoot))
                Directory.Move(backupRoot, targetRoot);
        }
    }

    private async Task<int> BuildStageAsync(string sourceRoot, string stageRoot, CancellationToken cancellationToken)
    {
        var sourceGraphRoot = Path.Combine(sourceRoot, "Graph");
        var sourceGraphPath = Path.Combine(sourceGraphRoot, "graph.json");
        if (!File.Exists(sourceGraphPath))
            throw new FileNotFoundException("Project Graph/graph.json was not found.", sourceGraphPath);

        var graph = JsonSerializer.Deserialize<EditorGraphDocument>(await File.ReadAllTextAsync(sourceGraphPath, cancellationToken).ConfigureAwait(false), JsonOptions)
            ?? throw new InvalidDataException("Project Graph/graph.json is empty.");
        var groups = graph.Nodes.Where(node => string.Equals(node.Type, "Group", StringComparison.OrdinalIgnoreCase)).ToArray();
        var sources = ResolveGroupSources(sourceGraphRoot, groups);
        ValidateRawSourceCoverage(sourceGraphRoot, sources.Values);

        CopyOptionalFile(sourceRoot, stageRoot, "settings.json");
        CopyOptionalDirectory(sourceRoot, stageRoot, "Assets", cancellationToken);
        CopyOptionalDirectory(sourceRoot, stageRoot, "I18n", cancellationToken);

        var targetGraphRoot = Path.Combine(stageRoot, "Graph");
        Directory.CreateDirectory(targetGraphRoot);
        await File.WriteAllTextAsync(Path.Combine(targetGraphRoot, "graph.json"), JsonSerializer.Serialize(graph, JsonOptions), cancellationToken).ConfigureAwait(false);
        var targetGroupsRoot = Path.Combine(targetGraphRoot, "groups");
        Directory.CreateDirectory(targetGroupsRoot);

        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rawPath = sources[group.Id];
            var raw = JsonSerializer.Deserialize<GroupDocument>(await File.ReadAllTextAsync(rawPath, cancellationToken).ConfigureAwait(false), JsonOptions)
                ?? throw new InvalidDataException($"Raw group '{DisplayPath(sourceGraphRoot, rawPath)}' is empty.");
            var compiled = GalgroupCompiler.Compile(raw, _entryCatalog).Document;
            var targetPath = Path.Combine(targetGroupsRoot, $"{group.Id}.galgroup");
            await File.WriteAllTextAsync(targetPath, JsonSerializer.Serialize(compiled, JsonOptions), cancellationToken).ConfigureAwait(false);
        }

        return groups.Length;
    }

    private static Dictionary<string, string> ResolveGroupSources(string graphRoot, IEnumerable<EditorGraphNodeDto> groups)
    {
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        var sourceOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            if (string.IsNullOrWhiteSpace(group.Id))
                throw new InvalidDataException("Every Group node must have a non-empty id.");
            if (sources.ContainsKey(group.Id))
                throw new InvalidDataException($"Duplicate Group node id '{group.Id}'.");

            var relative = string.IsNullOrWhiteSpace(group.File) ? $"groups/{group.Id}.rawgalgroup" : group.File!;
            if (!relative.EndsWith(".rawgalgroup", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Group '{group.Id}' must reference a .rawgalgroup source file.");
            if (Path.IsPathRooted(relative))
                throw new InvalidDataException($"Group '{group.Id}' source path must be relative to Graph/.");

            var sourcePath = Path.GetFullPath(Path.Combine(graphRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsSameOrChild(sourcePath, graphRoot))
                throw new InvalidDataException($"Group '{group.Id}' source path must remain under Graph/.");
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException($"Group '{group.Id}' source file is missing.", sourcePath);
            if (sourceOwners.TryGetValue(sourcePath, out var existingGroup))
                throw new InvalidDataException($"Groups '{existingGroup}' and '{group.Id}' reference the same Raw source '{DisplayPath(graphRoot, sourcePath)}'.");

            sourceOwners.Add(sourcePath, group.Id);
            sources.Add(group.Id, sourcePath);
        }

        return sources;
    }

    private static void ValidateRawSourceCoverage(string graphRoot, IEnumerable<string> referencedSources)
    {
        var referenced = referencedSources.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var orphan = Directory.EnumerateFiles(graphRoot, "*.rawgalgroup", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .FirstOrDefault(path => !referenced.Contains(path));
        if (orphan is not null)
            throw new InvalidDataException($"Raw group '{DisplayPath(graphRoot, orphan)}' is not referenced by a Group node.");
    }

    private static void CopyOptionalFile(string sourceRoot, string targetRoot, string relativePath)
    {
        var source = Path.Combine(sourceRoot, relativePath);
        if (!File.Exists(source)) return;
        var target = Path.Combine(targetRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, overwrite: true);
    }

    private static void CopyOptionalDirectory(string sourceRoot, string targetRoot, string relativePath, CancellationToken cancellationToken)
    {
        var sourceDirectory = Path.Combine(sourceRoot, relativePath);
        if (!Directory.Exists(sourceDirectory)) return;
        foreach (var source in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(sourceDirectory, source);
            var target = Path.Combine(targetRoot, relativePath, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: true);
        }
    }

    private static void ValidateOutputPath(string sourceRoot, string targetRoot)
    {
        if (string.Equals(sourceRoot, targetRoot, StringComparison.OrdinalIgnoreCase) || IsSameOrChild(sourceRoot, targetRoot))
            throw new InvalidDataException("Build output must not be the project directory or one of its ancestors.");

        foreach (var protectedDirectory in new[] { "Graph", "Assets", "I18n", ".galnet", "Temp" })
            if (IsSameOrChild(targetRoot, Path.Combine(sourceRoot, protectedDirectory)))
                throw new InvalidDataException($"Build output must not be inside project '{protectedDirectory}/'.");
    }

    private static bool IsSameOrChild(string candidate, string parent)
    {
        var relative = Path.GetRelativePath(parent, candidate);
        return relative.Length == 0 || (!Path.IsPathRooted(relative) && !relative.Equals("..", StringComparison.Ordinal) && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

    private static string DisplayPath(string basePath, string path) => Path.GetRelativePath(basePath, path).Replace('\\', '/');
}

public sealed record ProjectContentBuildResult(string OutputPath, int CompiledGroupCount);
