using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Contracts.Builtins;
using GalNet.Core.Assets;
using GalNet.Core.Entry;
using GalNet.Core.Gallery;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Shared.Commands;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Headless;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private static string _operationId = "cli";

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var operations = new ToolOperationCatalog();
            var invocation = CliInvocation.Parse(operations, args);
            _operationId = invocation.Definition.OperationId;
            if (_operationId == "help")
            {
                var definitions = ToolHelp.Select(operations, invocation);
                if (invocation.Has("--json"))
                    WriteJson(new { total = definitions.Count, operations = definitions });
                else Console.Out.Write(ToolHelp.RenderText(definitions, invocation.Positionals.Count != 0));
                return 0;
            }
            args = new[] { _operationId }.Concat(invocation.Positionals)
                .Concat(invocation.Options.SelectMany(pair => new[] { pair.Key, pair.Value })).ToArray();
            var catalog = new EditorCommandCatalog();
            var fileCatalog = new AssetFileCommandCatalog();
            var entryCatalog = new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules());
            if (args[0].Equals("project.create", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length < 2) return Fail("create requires a project path.");
                var nameIndex = Array.IndexOf(args, "--name");
                var name = nameIndex >= 0 && nameIndex + 1 < args.Length ? args[nameIndex + 1] : null;
                await EditorProjectCreator.CreateAsync(args[1], entryCatalog, name);
                WriteJson(new { success = true, projectPath = Path.GetFullPath(args[1]), name });
                return 0;
            }
            if (args.Length < 2) return Fail("A project path is required.");

            var verb = args[0].ToLowerInvariant();
            var projectPath = Path.GetFullPath(args[1]);
            if (verb == "project.build") return await BuildAsync(projectPath, args, entryCatalog);
            if (verb == "project.export") return await ExportAsync(projectPath, args);
            var document = Load(projectPath, entryCatalog);
            switch (verb)
            {
                case "project.info": WriteJson(CreateSummary(projectPath, document)); return 0;
                case "graph.nodes": WriteJson(document.Graph.Nodes.Select(NodeSummary)); return 0;
                case "graph.node": return WriteNode(document, args);
                case "group.entries": return WriteEntries(document, args);
                case "variables": WriteJson(new { player = document.Graph.PlayerVariables, save = document.Graph.SaveVariables }); return 0;
                case "project.validate":
                    var validation = EditorDocumentValidator.Validate(document);
                    WriteJson(new { success = validation.IsValid, diagnostics = validation.Diagnostics });
                    return validation.IsValid ? 0 : 4;
                case "execute":
                    if (args.Length < 3) return Fail("execute requires a JSON file path or '-'.");
                    var json = args[2] == "-" ? await Console.In.ReadToEndAsync() : await File.ReadAllTextAsync(args[2]);
                    return await ExecuteAsync(projectPath, document, catalog, fileCatalog, entryCatalog, json);
                default: return Fail($"Unknown command '{args[0]}'.");
            }
        }
        catch (ArgumentException exception) { return Fail(exception.Message); }
        catch (JsonException exception) { return Fail($"Invalid JSON: {exception.Message}"); }
        catch (Exception exception) { return Fail(exception.Message, 1); }
    }

    private static async Task<int> ExecuteAsync(string projectPath, EditorProjectDocument original,
        EditorCommandCatalog catalog, AssetFileCommandCatalog fileCatalog, IEntryCatalog entryCatalog, string json)
    {
        using var parsed = JsonDocument.Parse(json);
        var root = parsed.RootElement;
        var elements = root.TryGetProperty("commands", out var array) ? array.EnumerateArray().ToList() : [root];
        if (elements.Count == 0) return Fail("At least one command is required.", 5);

        var fileIds = fileCatalog.GetAll().Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var isFile = elements.Select(element => GetCommandId(element)).Select(fileIds.Contains).ToList();
        if (isFile.Any(value => value))
        {
            if (elements.Count != 1 || !isFile[0]) return Fail("File commands cannot be mixed with document commands.", 5);
            if (root.TryGetProperty("dryRun", out var fileDryRun) && fileDryRun.GetBoolean())
                return Fail("File operations do not support dryRun; no files were changed.", 5);
            var command = fileCatalog.Deserialize(GetCommandId(elements[0]), elements[0], JsonOptions);
            var result = await new AssetFileCommandExecutor(entryCatalog).ExecuteAsync(projectPath, command);
            WriteJson(result); return result.Success ? 0 : 5;
        }

        var working = GalNet.Editor.Abstraction.Changes.EditorDocumentCloner.Clone(original);
        var handler = new BuiltInEditorCommandHandler(entryCatalog);
        var changed = new List<string>();
        var descriptions = new List<string>();
        foreach (var element in elements)
        {
            var command = catalog.Deserialize(GetCommandId(element), element, JsonOptions);
            if (!handler.CanHandle(command)) return Fail($"Unsupported command '{command.CommandId}'.", 5);
            var execution = handler.Execute(working, command, new EditorCommandContext(0, false));
            if (!execution.Success) { WriteJson(new { success = false, diagnostics = execution.Diagnostics }); return 5; }
            changed.AddRange(execution.ChangedResources);
            if (execution.Description is not null) descriptions.Add(execution.Description.Description);
        }
        var validation = EditorDocumentValidator.Validate(working);
        if (!validation.IsValid) { WriteJson(new { success = false, diagnostics = validation.Diagnostics }); return 5; }
        var dryRun = root.TryGetProperty("dryRun", out var dryRunElement) && dryRunElement.GetBoolean();
        if (!dryRun) await new DirectProjectPersistence(projectPath, new EditorDocumentRepository(entryCatalog)).SaveAsync(working);
        WriteJson(new { success = true, dryRun, description = string.Join("; ", descriptions), changedResources = changed.Distinct(), diagnostics = validation.Diagnostics });
        return 0;
    }

    private static EditorProjectDocument Load(string projectPath, IEntryCatalog entryCatalog)
    {
        if (!Directory.Exists(projectPath)) throw new DirectoryNotFoundException($"Project directory not found: {projectPath}");
        return new DirectProjectPersistence(projectPath, new EditorDocumentRepository(entryCatalog)).Load();
    }

    private static async Task<int> ExportAsync(string projectPath, string[] args)
    {
        if (!Directory.Exists(projectPath)) throw new DirectoryNotFoundException($"Project directory not found: {projectPath}");
        var outputIndex = Array.IndexOf(args, "--output");
        if (outputIndex >= 0 && outputIndex + 1 >= args.Length) return Fail("--output requires a directory path.");
        var outputDirectory = outputIndex >= 0
            ? Path.GetFullPath(args[outputIndex + 1])
            : Path.Combine(projectPath, "Output");
        var projectName = Path.GetFileName(Path.TrimEndingDirectorySeparator(projectPath));
        var resourceTypes = BuiltinResourceTypes.CreateCatalog();
        var galleryTypes = BuiltinGalleryTypes.CreateCatalog(resourceTypes);
        var result = await GamePackageExporter.ExportAsync(projectName, projectName, projectPath, outputDirectory, resourceTypes, galleryTypes);
        WriteJson(new { success = result.Success, packagePath = result.PackagePath, error = result.Error });
        return result.Success ? 0 : 5;
    }

    private static async Task<int> BuildAsync(string projectPath, string[] args, IEntryCatalog entryCatalog)
    {
        if (!Directory.Exists(projectPath)) throw new DirectoryNotFoundException($"Project directory not found: {projectPath}");
        var outputIndex = Array.IndexOf(args, "--output");
        if (outputIndex >= 0 && outputIndex + 1 >= args.Length) return Fail("--output requires a directory path.");
        var outputDirectory = outputIndex >= 0 ? Path.GetFullPath(args[outputIndex + 1]) : null;
        var result = await new ProjectContentBuilder(entryCatalog).BuildAsync(projectPath, outputDirectory);
        WriteJson(new { success = true, outputPath = result.OutputPath, compiledGroupCount = result.CompiledGroupCount });
        return 0;
    }

    private static string GetCommandId(JsonElement element) =>
        element.TryGetProperty("commandId", out var id) && !string.IsNullOrWhiteSpace(id.GetString())
            ? id.GetString()! : throw new JsonException("Every command requires a non-empty commandId.");


    private static int WriteNode(EditorProjectDocument document, string[] args)
    {
        if (args.Length < 3) return Fail("node requires a node ID.");
        var node = document.Graph.Nodes.FirstOrDefault(item => item.Id == args[2]);
        if (node is null) return Fail($"Node '{args[2]}' does not exist.", 3);
        WriteJson(new { node, incomingEdges = document.Graph.Edges.Where(edge => edge.ToNodeId == node.Id), outgoingEdges = document.Graph.Edges.Where(edge => edge.FromNodeId == node.Id), entries = document.GroupEntries.GetValueOrDefault(node.Id) });
        return 0;
    }

    private static int WriteEntries(EditorProjectDocument document, string[] args)
    {
        if (args.Length < 3) return Fail("entries requires a group ID.");
        if (!document.GroupEntries.TryGetValue(args[2], out var entries)) return Fail($"Group '{args[2]}' does not exist or has no entries.", 3);
        WriteJson(entries); return 0;
    }

    private static object CreateSummary(string projectPath, EditorProjectDocument document)
    {
        var validation = EditorDocumentValidator.Validate(document);
        return new
        {
            project = new { name = document.Graph.Name, path = projectPath },
            graph = new { nodeCount = document.Graph.Nodes.Count, edgeCount = document.Graph.Edges.Count, entryCount = document.GroupEntries.Values.Sum(entries => entries.Count), rootNodeId = document.Graph.RootNodeId },
            variables = new { player = document.Graph.PlayerVariables.Count, save = document.Graph.SaveVariables.Count },
            diagnostics = new { errors = validation.Diagnostics.Count(item => item.Severity == EditorDiagnosticSeverity.Error), warnings = validation.Diagnostics.Count(item => item.Severity == EditorDiagnosticSeverity.Warning) }
        };
    }

    private static object NodeSummary(EditorGraphNodeDto node) => new { node.Id, node.Type, node.BranchType, node.Name, node.X, node.Y, optionCount = node.Options?.Count ?? 0, conditionCount = node.Conditions?.Count ?? 0 };
    private static void WriteJson(object value)
    {
        var data = JsonSerializer.SerializeToNode(value, JsonOptions);
        var success = true;
        var diagnostics = new List<ToolDiagnostic>();
        if (data is System.Text.Json.Nodes.JsonObject obj)
        {
            if (obj["success"] is { } status) success = status.GetValue<bool>();
            if (obj["diagnostics"] is System.Text.Json.Nodes.JsonArray entries)
            {
                foreach (var entry in entries)
                    if (entry is not null) diagnostics.Add(new ToolDiagnostic(
                        entry["code"]!.GetValue<string>(), entry["severity"]!.GetValue<string>().ToLowerInvariant(),
                        entry["message"]!.GetValue<string>(), entry["resource"]?.GetValue<string>()));
                obj.Remove("diagnostics");
            }
            obj.Remove("success");
        }
        Console.Out.WriteLine(JsonSerializer.Serialize(new ToolResult(_operationId, success, data, diagnostics), JsonOptions));
    }
    private static int Fail(string message, int code = 2)
    {
        Console.Out.WriteLine(JsonSerializer.Serialize(new ToolResult(_operationId, false, null,
            [new ToolDiagnostic(code == 2 ? "cli.arguments" : "cli.failed", "error", message)]), JsonOptions));
        return code;
    }
}
