using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Shared.Commands;
using GalNet.Editor.Shared.Services;
using GalNet.Editor.Shared.UI;

namespace GalNet.Editor.Headless;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "help" or "--help" or "-h") { PrintHelp(); return 0; }
            var catalog = new EditorCommandCatalog();
            var fileCatalog = new AssetFileCommandCatalog();
            if (args[0].Equals("commands", StringComparison.OrdinalIgnoreCase))
            {
                WriteJson(catalog.GetAll().Concat(fileCatalog.GetAll()).Select(CommandSummary));
                return 0;
            }
            if (args[0].Equals("create", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length < 2) return Fail("create requires a project path.");
                var nameIndex = Array.IndexOf(args, "--name");
                var name = nameIndex >= 0 && nameIndex + 1 < args.Length ? args[nameIndex + 1] : null;
                await EditorProjectCreator.CreateAsync(args[1], name);
                WriteJson(new { success = true, projectPath = Path.GetFullPath(args[1]), name });
                return 0;
            }
            if (args.Length < 2) return Fail("A project path is required.");

            var verb = args[0].ToLowerInvariant();
            var projectPath = Path.GetFullPath(args[1]);
            var document = Load(projectPath);
            switch (verb)
            {
                case "summary": WriteJson(CreateSummary(projectPath, document)); return 0;
                case "nodes": WriteJson(document.Graph.Nodes.Select(NodeSummary)); return 0;
                case "node": return WriteNode(document, args);
                case "entries": return WriteEntries(document, args);
                case "variables": WriteJson(new { player = document.Graph.PlayerVariables, save = document.Graph.SaveVariables }); return 0;
                case "validate":
                    var validation = new EditorDocumentValidator().Validate(document);
                    WriteJson(new { success = validation.IsValid, diagnostics = validation.Diagnostics });
                    return validation.IsValid ? 0 : 4;
                case "execute":
                    if (args.Length < 3) return Fail("execute requires a JSON file path or '-'.");
                    var json = args[2] == "-" ? await Console.In.ReadToEndAsync() : await File.ReadAllTextAsync(args[2]);
                    return await ExecuteAsync(projectPath, document, catalog, fileCatalog, json);
                default: return Fail($"Unknown command '{args[0]}'.");
            }
        }
        catch (JsonException exception) { return Fail($"Invalid JSON: {exception.Message}"); }
        catch (Exception exception) { return Fail(exception.Message, 1); }
    }

    private static async Task<int> ExecuteAsync(string projectPath, EditorProjectDocument original,
        IEditorCommandCatalog catalog, IProjectFileCommandCatalog fileCatalog, string json)
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
            var command = fileCatalog.Deserialize(GetCommandId(elements[0]), elements[0], JsonOptions);
            var result = await new AssetFileCommandExecutor().ExecuteAsync(projectPath, command);
            WriteJson(result); return result.Success ? 0 : 5;
        }

        var working = GalNet.Editor.Abstraction.Changes.EditorDocumentCloner.Clone(original);
        var handler = new BuiltInEditorCommandHandler();
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
        var validation = new EditorDocumentValidator().Validate(working);
        if (!validation.IsValid) { WriteJson(new { success = false, diagnostics = validation.Diagnostics }); return 5; }
        var dryRun = root.TryGetProperty("dryRun", out var dryRunElement) && dryRunElement.GetBoolean();
        if (!dryRun) await new DirectProjectPersistence(projectPath).SaveAsync(working);
        WriteJson(new { success = true, dryRun, description = string.Join("; ", descriptions), changedResources = changed.Distinct(), diagnostics = validation.Diagnostics });
        return 0;
    }

    private static EditorProjectDocument Load(string projectPath)
    {
        if (!Directory.Exists(projectPath)) throw new DirectoryNotFoundException($"Project directory not found: {projectPath}");
        return new DirectProjectPersistence(projectPath).Load();
    }

    private static string GetCommandId(JsonElement element) =>
        element.TryGetProperty("commandId", out var id) && !string.IsNullOrWhiteSpace(id.GetString())
            ? id.GetString()! : throw new JsonException("Every command requires a non-empty commandId.");

    private static object CommandSummary(IEditorCommandDefinition definition) => new
    {
        id = definition.Id, definition.Description, displayNameKey = definition.DisplayNameKey.Key,
        parameters = definition is IProjectCommandDefinition project ? project.Schema.Parameters : null
    };

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
        var validation = new EditorDocumentValidator().Validate(document);
        return new
        {
            project = new { name = document.Graph.Name, path = projectPath },
            graph = new { nodeCount = document.Graph.Nodes.Count, edgeCount = document.Graph.Edges.Count, entryCount = document.GroupEntries.Values.Sum(entries => entries.Count), rootNodeId = document.Graph.RootNodeId },
            variables = new { player = document.Graph.PlayerVariables.Count, save = document.Graph.SaveVariables.Count },
            diagnostics = new { errors = validation.Diagnostics.Count(item => item.Severity == EditorDiagnosticSeverity.Error), warnings = validation.Diagnostics.Count(item => item.Severity == EditorDiagnosticSeverity.Warning) }
        };
    }

    private static object NodeSummary(EditorGraphNodeDto node) => new { node.Id, node.Type, node.BranchType, node.Name, node.X, node.Y, optionCount = node.Options?.Count ?? 0, conditionCount = node.Conditions?.Count ?? 0 };
    private static void WriteJson(object value) => Console.Out.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
    private static int Fail(string message, int code = 2) { Console.Error.WriteLine(message); return code; }
    private static void PrintHelp() => Console.WriteLine("""
        GalNet Editor Headless

        Usage:
          galnet-editor-headless commands
          galnet-editor-headless create <project> [--name <name>]
          galnet-editor-headless summary|nodes|variables|validate <project>
          galnet-editor-headless node <project> <node-id>
          galnet-editor-headless entries <project> <group-id>
          galnet-editor-headless execute <project> <command-file|->

        Each invocation loads project files, applies one transaction, validates it, and writes it back.
        There is no persistent session, revision, Undo/Redo, or JSON-lines server.
        """);
}
