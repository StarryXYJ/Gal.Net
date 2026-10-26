using GalNet.Contracts.Builtins;
using System.Text.Json;
using GalNet.Core.Entry;
using GalNet.Core.Configuration;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Project;
using GalNet.Editor.Shared.Services;
using GalNet.Assets;
using GalNet.Editor.Shared.Visuals;

namespace GalNet.Editor.Shared.Commands;

public static class EditorProjectCreator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<ToolResult> CreateAsync(ProjectCreateRequest request, ProjectConfiguration? settings = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Path);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Path));
        if (root == Path.GetPathRoot(root)) throw new ArgumentException("A filesystem root cannot be a project target.");
        var parent = Path.GetDirectoryName(root)!;
        FileSystemPaths.RequireNoLinks(root, "Project creation cannot traverse filesystem links.");
        EnsureEmptyTarget(root);
        var name = request.Name is null ? Path.GetFileName(root) : request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Project display name is required.");
        using var contract = request.ContractsDirectory is null ? null : ProjectSchemaContext.LoadContractsDirectory(request.ContractsDirectory);
        var contractFiles = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (request.ContractsDirectory is not null)
        {
            var reader = new ProjectRawReader(request.ContractsDirectory);
            var diagnostics = new List<ToolDiagnostic>();
            foreach (var file in reader.Files(".", diagnostics)) contractFiles.Add(file, File.ReadAllBytes(reader.Resolve(file)));
            if (diagnostics.Count != 0) throw new InvalidDataException("Contract directory contains unreadable files or filesystem links.");
        }
        var entryId = Guid.NewGuid().ToString("N");
        var groupId = Guid.NewGuid().ToString("N");
        var configuration = (settings ?? DefaultSettings()).Clone();
        // A custom contract opts in by declaring this authoring field and effect capability.
        var includeVisuals = contract is not null && contract.Catalog.TryGet(BuiltinEntryIds.ApplyEffect, out _) &&
            contract.Configurations.Sections.Any(section => section.Id == DefaultProjectConfiguration.Section.Id &&
                section.Fields.Any(field => field.Id == "defaultVisualPrograms"));
        var visuals = includeVisuals ? DefaultVisualTemplate.Prepare(configuration) : null;
        configuration = visuals?.Configuration ?? configuration;
        var files = new[] { "Graph/graph.json", $"Graph/groups/{groupId}.rawgalgroup", "settings.json", ".galnet/editor-state.json" }
            .Concat(contractFiles.Keys.Select(file => "Contracts/" + file))
            .Concat(visuals?.Files.Keys ?? Enumerable.Empty<string>())
            .Concat(contract is null ? ["Contracts/README.md"] : Array.Empty<string>()).Order(StringComparer.Ordinal).ToArray();
        var report = new ProjectCreateReport(root, name, request.DryRun, contract is not null, contract?.Fingerprint, files);
        if (request.DryRun) return new("project.create", true, report, []);
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(parent);
        var stage = Path.Combine(parent, ".galnet-create-" + Guid.NewGuid().ToString("N"));
        if (Path.GetDirectoryName(Path.GetFullPath(stage)) != parent || !Path.GetFileName(stage).StartsWith(".galnet-create-", StringComparison.Ordinal))
            throw new InvalidOperationException("Creation staging escaped its parent.");
        var removedEmptyTarget = false;
        try
        {
            foreach (var directory in new[] { "Graph/groups", "Assets/Layer", "Assets/Audio", "Assets/Video", "I18n", "Output", "Temp", ".galnet", "Contracts" })
                Directory.CreateDirectory(Path.Combine(stage, directory));
            foreach (var (file, bytes) in contractFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = new ProjectRawReader(stage).Resolve("Contracts/" + file);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(path, bytes, cancellationToken);
            }
            foreach (var (file, bytes) in visuals?.Files ?? new Dictionary<string, byte[]>())
            {
                var path = new ProjectRawReader(stage).Resolve(file);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(path, bytes, cancellationToken);
            }
            if (contract is null)
                await File.WriteAllTextAsync(Path.Combine(stage, "Contracts/README.md"),
                    "Deploy GalNet.Project.Contracts.dll and its managed dependencies here. The public static GalNet.Project.Contracts.ProjectSchemas.Modules property declares this project's schemas. No built-in fallback is used.\n", cancellationToken);
            using var deployed = contract is null ? null : ProjectSchemaContext.Load(stage);
            var catalog = deployed?.Catalog ?? new TargetProfileEntryCatalog([]);
            var document = new EditorProjectDocument
            {
                Configuration = configuration,
                Graph = new EditorGraphDocument
                {
                    Name = name,
                    RootNodeId = entryId,
                    Nodes =
                    [
                        new EditorGraphNodeDto { Id = entryId, Type = "Entry", Name = "Entry", X = 4620, Y = 4950 },
                        new EditorGraphNodeDto { Id = groupId, Type = "Group", Name = "Opening", X = 4900, Y = 4950, File = $"groups/{groupId}.rawgalgroup" }
                    ],
                    Edges = [new EditorGraphEdgeDto { Id = Guid.NewGuid().ToString("N"), FromNodeId = entryId, FromOutlet = 0, ToNodeId = groupId }]
                },
                GroupEntries = { [groupId] = [] }
            };
            await new DirectProjectPersistence(stage, new EditorDocumentRepository(catalog), contract?.Configurations).SaveAsync(document, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(stage, ".galnet/editor-state.json"), JsonSerializer.Serialize(new EditorProjectState(), JsonOptions), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureEmptyTarget(root);
            if (Directory.Exists(root)) { Directory.Delete(root); removedEmptyTarget = true; }
            Directory.Move(stage, root);
            return new("project.create", true, report, []);
        }
        catch
        {
            if (removedEmptyTarget && !Directory.Exists(root) && !File.Exists(root)) Directory.CreateDirectory(root);
            throw;
        }
        finally
        {
            if (Directory.Exists(stage))
            {
                Directory.Delete(stage, recursive: true);
            }
        }
    }

    private static ProjectConfiguration DefaultSettings()
    {
        var audio = GalNet.Contracts.Builtins.BuiltinAudioDefaults.Create();
        return new GalNet.Contracts.Builtins.DefaultProjectOptions { AudioGroups = audio.Groups.ToList(), AudioTracks = audio.Tracks.ToList() }.ToConfiguration();
    }

    private static void EnsureEmptyTarget(string root)
    {
        if (File.Exists(root) || Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
            throw new IOException($"Project target must be absent or an empty directory: '{root}'.");
    }
}
