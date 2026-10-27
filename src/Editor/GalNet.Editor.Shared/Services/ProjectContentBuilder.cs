using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Assets;
using GalNet.Contracts.Builtins.Assets;
using GalNet.Core.Assets;
using GalNet.Core.Compilation;
using GalNet.Core.Configuration;
using GalNet.Core.Entry;
using GalNet.Core.Serialization;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Shared.Commands;

namespace GalNet.Editor.Shared.Services;

/// <summary>Builds editable project content into the compiled directory layout consumed by Runtime.</summary>
public sealed class ProjectContentBuilder(IEntryCatalog entryCatalog, IResourceTypeCatalog? resourceTypes = null,
    ProjectConfigurationSchema? configurations = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IEntryCatalog _entryCatalog = entryCatalog ?? throw new ArgumentNullException(nameof(entryCatalog));

    public async Task<ProjectContentBuildResult> BuildAsync(
        string projectRoot,
        string? outputRoot = null,
        CancellationToken cancellationToken = default)
    {
        var (build, _) = await BuildPreparedAsync(projectRoot, outputRoot, (stage, token) =>
        {
            token.ThrowIfCancellationRequested();
            var diagnostics = ImageVariantsValidation.Validate(stage, resourceTypes ?? BuiltinResourceTypes.CreateCatalog());
            if (diagnostics.Any(item => item.Severity == "error"))
                throw new InvalidDataException(string.Join("; ", diagnostics.Select(item => $"{item.File}{item.JsonPointer}: {item.Message}")));
            return Task.FromResult(true);
        }, cancellationToken).ConfigureAwait(false);
        return build;
    }

    /// <summary>Prepares and validates assets from the private stage before publication; prepared values must own their bytes.</summary>
    internal async Task<(ProjectContentBuildResult Build, T Prepared)> BuildPreparedAsync<T>(
        string projectRoot, string? outputRoot, Func<string, CancellationToken, Task<T>> prepare,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        var sourceRoot = Path.GetFullPath(projectRoot);
        if (!Directory.Exists(sourceRoot))
            throw new DirectoryNotFoundException($"Project directory not found: {sourceRoot}");

        var targetRoot = Path.GetFullPath(outputRoot ?? Path.Combine(sourceRoot, "Output"));
        ValidateOutputPath(sourceRoot, targetRoot);
        cancellationToken.ThrowIfCancellationRequested();
        if (configurations is not null) configurations.Validate(DirectProjectPersistence.LoadSettings(sourceRoot));
        var validation = new ProjectValidationService(sourceRoot).Validate("graph");
        var diagnostics = validation.Diagnostics.ToList();
        diagnostics.AddRange(LocalizationValidation.ValidateDisk(sourceRoot, _entryCatalog));
        _ = new ProjectRawReader(sourceRoot).Files(null, diagnostics);
        if (!validation.Success || diagnostics.Any(item => item.Code == "file.linkSkipped" || item.Severity == "error"))
            throw new InvalidDataException(string.Join("; ", diagnostics.Select(item => $"{item.File}{item.JsonPointer}: {item.Message}")));

        var stageRoot = Path.Combine(
            Path.GetDirectoryName(targetRoot) ?? throw new InvalidOperationException("Build output must have a parent directory."),
            $".{Path.GetFileName(targetRoot)}.build-{Guid.NewGuid():N}");
        string? backupRoot = null;
        try
        {
            Directory.CreateDirectory(stageRoot);
            var result = await BuildStageAsync(sourceRoot, stageRoot, cancellationToken).ConfigureAwait(false);
            var prepared = await prepare(stageRoot, cancellationToken).ConfigureAwait(false);
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

            return (result with { OutputPath = targetRoot, Diagnostics = diagnostics.ToArray() }, prepared);
        }
        finally
        {
            if (!string.IsNullOrEmpty(stageRoot) && Directory.Exists(stageRoot))
                Directory.Delete(stageRoot, recursive: true);
            if (backupRoot is not null && Directory.Exists(backupRoot) && !Directory.Exists(targetRoot))
                Directory.Move(backupRoot, targetRoot);
        }
    }

    private async Task<ProjectContentBuildResult> BuildStageAsync(string sourceRoot, string stageRoot, CancellationToken cancellationToken)
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

        var mappings = new List<ProjectCompiledSourceEntry>();
        var authors = new Dictionary<string, List<EditorEntryData>>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rawPath = sources[group.Id];
            var diagnostics = new List<GalNet.Editor.Abstraction.Commands.ToolDiagnostic>();
            var raw = new ProjectValidationService(sourceRoot).ValidateGroup(DisplayPath(sourceRoot, rawPath), diagnostics, out var compilation, _entryCatalog)
                ?? throw new InvalidDataException(string.Join("; ", diagnostics.Select(item => $"{item.File}{item.JsonPointer}: {item.Message}")));
            var compiled = compilation!.Document;
            var offset = 0;
            foreach (var entry in raw.Entries)
            {
                var primitives = compilation.SourceMap[entry.Id];
                mappings.Add(new(group.Id, entry.Id, offset, offset + primitives.Count, Array.AsReadOnly(primitives.ToArray())));
                offset += primitives.Count;
            }
            authors.Add(group.Id, raw.Entries.Select(entry => new EditorEntryData
            { StableId = entry.Id, Type = entry.Type, Condition = entry.Condition, Parameters = entry.Parameters }).ToList());
            var targetPath = Path.Combine(targetGroupsRoot, $"{group.Id}.galgroup");
            await File.WriteAllTextAsync(targetPath, JsonSerializer.Serialize(compiled, JsonOptions), cancellationToken).ConfigureAwait(false);
        }

        return new(stageRoot, groups.Length) { SourceEntries = mappings.AsReadOnly(), AuthorSignature = ProjectAuthorIdentity.Compute(graph, authors) };
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

    internal static void ValidateOutputPath(string sourceRoot, string targetRoot)
    {
        if (IsSameOrChild(sourceRoot, targetRoot))
            throw new InvalidDataException("Build output must not be the project directory or one of its ancestors.");

        foreach (var protectedDirectory in new[] { "Graph", "Assets", "I18n", ".galnet", "Temp", "Contracts", ".git" })
            if (IsSameOrChild(targetRoot, Path.Combine(sourceRoot, protectedDirectory)))
                throw new InvalidDataException($"Build output must not be inside project '{protectedDirectory}/'.");
        FileSystemPaths.RequireNoLinks(targetRoot, "Build/export output must not traverse filesystem links.");
    }

    private static bool IsSameOrChild(string candidate, string parent) => FileSystemPaths.IsSameOrChild(candidate, parent);

    private static string DisplayPath(string basePath, string path) => Path.GetRelativePath(basePath, path).Replace('\\', '/');
}

public sealed record ProjectContentBuildResult(string OutputPath, int CompiledGroupCount)
{
    public IReadOnlyList<GalNet.Editor.Abstraction.Commands.ToolDiagnostic> Diagnostics { get; init; } = [];
    [JsonIgnore] public IReadOnlyList<ProjectCompiledSourceEntry> SourceEntries { get; init; } = [];
    [JsonIgnore] public string AuthorSignature { get; init; } = "";
}

public sealed record ProjectCompiledSourceEntry(string GroupId, string EntryId, int Start, int End, IReadOnlyList<string> PrimitiveIds);
