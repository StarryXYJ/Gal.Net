using System.Text.Json;
using GalNet.Core.Entry;
using GalNet.Core.Primitives;
using GalNet.Core.Serialization;
using GalNet.Core.Settings;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Shared.Commands;

namespace GalNet.Editor.Shared.Services;

public sealed class EditorDocumentRepository(IEntryCatalog catalog) : IEditorDocumentRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private Dictionary<string, byte[]?>? _baseline;

    public LoadedEditorProjectDocument Load(string projectPath, string projectName, ProjectSettings settings)
    {
        var reader = new ProjectRawReader(projectPath);
        var validator = new ProjectValidationService(projectPath);
        var diagnostics = new List<ToolDiagnostic>();
        var rawGraph = reader.Read("Graph/graph.json", diagnostics);
        validator.ValidateGraph(rawGraph, diagnostics);
        RequireValid(diagnostics);
        var document = rawGraph!.Value.Deserialize<EditorGraphDocument>()!;
        var loaded = new LoadedEditorProjectDocument { Document = document };
        _baseline = new(PathComparer) { ["Graph/graph.json"] = File.ReadAllBytes(reader.Resolve("Graph/graph.json")) };
        foreach (var node in document.Nodes.Where(node => node.Type == "Group"))
        {
            var file = GroupPath(reader, node.File!);
            var group = validator.ValidateGroup(file, diagnostics, catalog);
            RequireValid(diagnostics);
            _baseline[file] = File.ReadAllBytes(reader.Resolve(file));
            foreach (var entry in group!.Entries)
                foreach (var (key, value) in entry.Parameters)
                    if (!JsonElement.DeepEquals(value, DynamicParameterValue.FromEditorValue(DynamicParameterValue.ToEditorValue(value), catalog.Get(entry.Type).DynamicParameters![key].ValueType)))
                        throw new InvalidDataException($"Parameter '{key}' in '{file}' cannot round-trip through the editor value representation; edit the raw file directly.");
            loaded.GroupEntries[node.Id] = group!.Entries.Select((entry, index) => new EditorEntryData
            {
                StableId = entry.Id,
                Id = index + 1,
                Type = entry.Type,
                Condition = entry.Condition,
                Parameters = entry.Parameters.ToDictionary(pair => pair.Key, pair => DynamicParameterValue.ToEditorValue(pair.Value), StringComparer.Ordinal)
            }).ToList();
        }
        return loaded;
    }

    public ProjectSavePlan PrepareSave(string projectPath, EditorGraphDocument document,
        IReadOnlyDictionary<string, IReadOnlyList<EditorEntryData>> groupEntries)
    {
        var reader = new ProjectRawReader(projectPath);
        EnsureUnchanged(reader);
        if (_baseline is null && File.Exists(reader.Resolve("Graph/graph.json"))) _ = Load(projectPath, document.Name, new ProjectSettings());
        var diagnostics = new List<ToolDiagnostic>();
        var validator = new ProjectValidationService(projectPath);
        var graph = JsonSerializer.SerializeToElement(document, JsonOptions);
        validator.ValidateGraph(graph, diagnostics, checkSourceReferences: false);
        var writes = new Dictionary<string, byte[]>(PathComparer);
        var owners = new HashSet<string>(PathComparer);
        foreach (var node in document.Nodes.Where(node => node.Type == "Group"))
        {
            if (string.IsNullOrWhiteSpace(node.File)) throw new InvalidDataException("Group source path is required.");
            var file = GroupPath(reader, node.File);
            if (!owners.Add(file)) throw new InvalidDataException($"Multiple Groups reference '{file}'.");
            if (!groupEntries.TryGetValue(node.Id, out var entries)) throw new InvalidDataException($"Group '{node.Id}' has no entry document.");
            var raw = JsonSerializer.SerializeToElement(new GroupDocument { Kind = GroupDocumentKind.Raw, Entries = entries.Select(SerializeEntry).ToList() }, JsonOptions);
            _ = ProjectValidationService.ValidateGroupDocument(raw, file, diagnostics, catalog);
            if (File.Exists(reader.Resolve(file)) && _baseline is not null && !_baseline.ContainsKey(file))
                throw new IOException($"New Group source would overwrite an existing unowned file: '{file}'.");
            writes.Add(file, JsonSerializer.SerializeToUtf8Bytes(raw, JsonOptions));
        }
        RequireValid(diagnostics);
        writes.Add("Graph/graph.json", JsonSerializer.SerializeToUtf8Bytes(graph, JsonOptions));
        var removed = new List<string>();
        var oldGraphPath = reader.Resolve("Graph/graph.json");
        if (File.Exists(oldGraphPath))
        {
            using var old = JsonDocument.Parse(File.ReadAllBytes(oldGraphPath));
            validator.ValidateGraph(old.RootElement, diagnostics);
            RequireValid(diagnostics);
            foreach (var node in old.RootElement.Deserialize<EditorGraphDocument>()!.Nodes.Where(node => node.Type == "Group"))
            {
                var file = GroupPath(reader, node.File!);
                if (!owners.Contains(file) && !groupEntries.ContainsKey(node.Id)) removed.Add(file);
            }
        }
        return new(writes, removed);
    }

    public void Save(string projectPath, EditorGraphDocument document, IReadOnlyDictionary<string, IReadOnlyList<EditorEntryData>> groupEntries)
    {
        var plan = PrepareSave(projectPath, document, groupEntries);
        ApplySave(projectPath, plan);
    }

    public void ApplySave(string projectPath, ProjectSavePlan plan, CancellationToken cancellationToken = default)
    {
        EnsureUnchanged(new ProjectRawReader(projectPath));
        ProjectAuthorFileTransaction.Commit(projectPath, plan.Writes, plan.DeletedFiles, cancellationToken);
        _baseline ??= new(PathComparer);
        foreach (var (file, bytes) in plan.Writes) _baseline[file] = bytes.ToArray();
        foreach (var file in plan.DeletedFiles) _baseline.Remove(file);
    }

    public GroupEntryDocument SerializeEntry(EditorEntryData entry)
    {
        var definition = catalog.Get(entry.Type);
        if (string.IsNullOrWhiteSpace(entry.StableId)) throw new InvalidDataException("Entry stable ID is required.");
        foreach (var key in entry.Parameters.Keys)
            if (!definition.Parameters.ContainsKey(key)) throw new InvalidDataException($"Unknown parameter '{key}' for '{entry.Type}'.");
        if (entry.Parameters.Values.Any(value => value is null)) throw new InvalidDataException("Editor parameter values must be strings; use patchParameters null to remove a parameter.");
        return new GroupEntryDocument
        {
            Id = entry.StableId,
            Type = entry.Type,
            Condition = entry.Condition,
            Parameters = entry.Parameters.ToDictionary(pair => pair.Key,
                pair => DynamicParameterValue.FromEditorValue(pair.Value, definition.DynamicParameters![pair.Key].ValueType), StringComparer.Ordinal)
        };
    }

    private void EnsureUnchanged(ProjectRawReader reader)
    {
        if (_baseline is null) return;
        foreach (var (file, original) in _baseline)
        {
            var path = reader.Resolve(file);
            var current = File.Exists(path) ? File.ReadAllBytes(path) : null;
            if (!(original is null ? current is null : current is not null && original.AsSpan().SequenceEqual(current)))
                throw new IOException($"Project source changed after loading: '{file}'. Reload before saving.");
        }
    }

    private static string GroupPath(ProjectRawReader reader, string file)
    {
        if (Path.IsPathRooted(file)) throw new InvalidDataException("Group source must be relative to Graph/.");
        var relative = reader.Relative(reader.Resolve(Path.Combine("Graph", file)));
        if (!relative.StartsWith("Graph/", StringComparison.Ordinal) || !relative.EndsWith(".rawgalgroup", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Group source must be a .rawgalgroup within Graph/.");
        return relative;
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static void RequireValid(IEnumerable<ToolDiagnostic> diagnostics)
    {
        var errors = diagnostics.Where(item => item.Severity == "error").ToArray();
        if (errors.Length != 0) throw new InvalidDataException(string.Join("\n", errors.Select(item => $"{item.File}{item.JsonPointer}: {item.Code}: {item.Message}")));
    }
}
