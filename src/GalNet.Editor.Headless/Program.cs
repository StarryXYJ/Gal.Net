using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Sessions;
using GalNet.Editor.Shared.Commands;

namespace GalNet.Editor.Headless;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private static readonly JsonSerializerOptions JsonLinesOptions = new(JsonOptions) { WriteIndented = false };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
            {
                PrintHelp();
                return 0;
            }

            var catalog = new EditorCommandCatalog();
            var fileCatalog = new AssetFileCommandCatalog();
            var fileExecutor = new AssetFileCommandExecutor();
            if (args[0].Equals("commands", StringComparison.OrdinalIgnoreCase))
            {
                WriteJson(catalog.GetAll().Concat(fileCatalog.GetAll()).Select(definition => new
                {
                    id = definition.Id,
                    description = definition.Description,
                    displayNameKey = definition.DisplayNameKey.Key,
                    parameters = definition.Schema.Parameters
                }));
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

            if (args.Length < 2)
                return Fail("A project path is required.");

            var verb = args[0].ToLowerInvariant();
            var projectPath = Path.GetFullPath(args[1]);
            var session = new EditorSessionFactory().Open(projectPath);

            switch (verb)
            {
                case "summary":
                    WriteJson(CreateSummary(projectPath, session));
                    return 0;
                case "nodes":
                    WriteJson(session.Document.Graph.Nodes.Select(NodeSummary));
                    return 0;
                case "node":
                    if (args.Length < 3) return Fail("node requires a node ID.");
                    var node = session.Document.Graph.Nodes.FirstOrDefault(item => item.Id == args[2]);
                    if (node is null) return Fail($"Node '{args[2]}' does not exist.", 3);
                    WriteJson(new
                    {
                        node,
                        incomingEdges = session.Document.Graph.Edges.Where(edge => edge.ToNodeId == node.Id),
                        outgoingEdges = session.Document.Graph.Edges.Where(edge => edge.FromNodeId == node.Id),
                        entries = session.Document.GroupEntries.GetValueOrDefault(node.Id)
                    });
                    return 0;
                case "entries":
                    if (args.Length < 3) return Fail("entries requires a group ID.");
                    if (!session.Document.GroupEntries.TryGetValue(args[2], out var entries))
                        return Fail($"Group '{args[2]}' does not exist or has no entry document.", 3);
                    WriteJson(entries);
                    return 0;
                case "variables":
                    WriteJson(new
                    {
                        player = session.Document.Graph.PlayerVariables,
                        save = session.Document.Graph.SaveVariables
                    });
                    return 0;
                case "validate":
                    var validation = session.Validate();
                    WriteJson(new { success = validation.IsValid, diagnostics = validation.Diagnostics });
                    return validation.IsValid ? 0 : 4;
                case "execute":
                    if (args.Length < 3) return Fail("execute requires a JSON file path or '-'.");
                    var json = args[2] == "-" ? await Console.In.ReadToEndAsync() : await File.ReadAllTextAsync(args[2]);
                    return await ExecuteEnvelopeAsync(projectPath, session, catalog, fileCatalog, fileExecutor, json, saveByDefault: !args.Contains("--no-save"));
                case "serve":
                    return await RunJsonLinesAsync(projectPath, session, catalog, fileCatalog, fileExecutor);
                default:
                    return Fail($"Unknown command '{args[0]}'.");
            }
        }
        catch (JsonException exception)
        {
            return Fail($"Invalid JSON: {exception.Message}", 2);
        }
        catch (Exception exception)
        {
            return Fail(exception.Message, 1);
        }
    }

    private static async Task<int> ExecuteEnvelopeAsync(
        string projectPath,
        IEditorSession session,
        IEditorCommandCatalog catalog,
        IProjectFileCommandCatalog fileCatalog,
        IProjectFileCommandExecutor fileExecutor,
        string json,
        bool saveByDefault)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var elements = CommandElements(root);
        if (elements.Any(element => IsFileCommand(element, fileCatalog)))
        {
            if (elements.Count != 1 || !IsFileCommand(elements[0], fileCatalog))
                return Fail("File commands cannot be mixed with document commands in one transaction.", 5);
            var commandId = elements[0].GetProperty("commandId").GetString()!;
            var fileResult = await fileExecutor.ExecuteAsync(projectPath, fileCatalog.Deserialize(commandId, elements[0], JsonOptions));
            WriteJson(fileResult);
            return fileResult.Success ? 0 : 5;
        }
        var commands = DeserializeCommands(root, catalog);
        var dryRun = root.TryGetProperty("dryRun", out var dryRunElement) && dryRunElement.GetBoolean();
        long? expectedRevision = root.TryGetProperty("expectedRevision", out var revisionElement)
            ? revisionElement.GetInt64()
            : null;
        var result = session.ExecuteTransaction(commands, new ExecuteOptions(expectedRevision, dryRun));
        if (result.Success && !dryRun && saveByDefault)
            await session.SaveAsync();
        WriteJson(result);
        return result.Success ? 0 : 5;
    }

    private static IReadOnlyList<IProjectEditCommand> DeserializeCommands(
        JsonElement root,
        IEditorCommandCatalog catalog)
    {
        var elements = CommandElements(root);
        var commands = new List<IProjectEditCommand>();
        foreach (var element in elements)
        {
            if (!element.TryGetProperty("commandId", out var commandIdElement) || string.IsNullOrWhiteSpace(commandIdElement.GetString()))
                throw new JsonException("Every command requires a non-empty commandId.");
            commands.Add(catalog.Deserialize(commandIdElement.GetString()!, element, JsonOptions));
        }
        return commands;
    }

    private static List<JsonElement> CommandElements(JsonElement root) => root.TryGetProperty("commands", out var commandsElement)
        ? commandsElement.EnumerateArray().ToList()
        : [root];

    private static bool IsFileCommand(JsonElement element, IProjectFileCommandCatalog catalog)
    {
        if (!element.TryGetProperty("commandId", out var id)) return false;
        return catalog.GetAll().Any(item => string.Equals(item.Id, id.GetString(), StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<int> RunJsonLinesAsync(
        string projectPath,
        IEditorSession session,
        IEditorCommandCatalog catalog,
        IProjectFileCommandCatalog fileCatalog,
        IProjectFileCommandExecutor fileExecutor)
    {
        Console.Error.WriteLine($"GalNet Editor Headless JSON Lines session ready: {projectPath}");
        string? line;
        while ((line = await Console.In.ReadLineAsync()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            object response;
            string? requestId = null;
            try
            {
                using var request = JsonDocument.Parse(line);
                requestId = request.RootElement.TryGetProperty("requestId", out var id) ? id.ToString() : null;
                response = await HandleRequestAsync(projectPath, session, catalog, fileCatalog, fileExecutor, request.RootElement);
            }
            catch (Exception exception)
            {
                response = new { requestId, success = false, error = exception.Message };
            }
            Console.Out.WriteLine(JsonSerializer.Serialize(response, JsonLinesOptions));
            await Console.Out.FlushAsync();
        }
        return 0;
    }

    private static async Task<object> HandleRequestAsync(
        string projectPath,
        IEditorSession session,
        IEditorCommandCatalog catalog,
        IProjectFileCommandCatalog fileCatalog,
        IProjectFileCommandExecutor fileExecutor,
        JsonElement request)
    {
        var requestId = request.TryGetProperty("requestId", out var id) ? id.ToString() : null;
        var method = request.GetProperty("method").GetString() ?? throw new JsonException("method is required.");
        object result = method switch
        {
            "project.summary" => CreateSummary(projectPath, session),
            "graph.listNodes" => Paginate(session.Document.Graph.Nodes.Select(NodeSummary).ToList(), request),
            "graph.getNode" => GetNode(session, request),
            "graph.getNeighbors" => GetNeighbors(session, request),
            "graph.findPath" => FindPath(session, request),
            "group.listEntries" => Paginate(GetEntries(session, request), request),
            "group.getEntry" => GetEntry(session, request),
            "variable.list" => ListVariables(session, request),
            "variable.findReferences" => Paginate(FindVariableReferences(session, request), request),
            "project.getSettings" => session.Document.Settings,
            "ui.getProject" => session.Document.UiProject,
            "asset.search" => SearchAssets(projectPath, request),
            "document.exportSnapshot" => session.Document,
            "command.list" => Paginate(catalog.GetAll().Concat(fileCatalog.GetAll()).Select(definition => new
            {
                id = definition.Id,
                description = definition.Description,
                displayNameKey = definition.DisplayNameKey.Key,
                parameters = definition.Schema.Parameters
            }).ToList(), request),
            "diagnostics.validate" => session.Validate(),
            "command.execute" => await ExecuteRequestAsync(projectPath, session, catalog, fileCatalog, fileExecutor, request),
            "history.undo" => session.Undo(),
            "history.redo" => session.Redo(),
            "project.save" => await SaveAndReturnAsync(session),
            _ => throw new InvalidOperationException($"Unknown method '{method}'.")
        };
        var success = result switch
        {
            CommandResult commandResult => commandResult.Success,
            FileCommandResult fileCommandResult => fileCommandResult.Success,
            ValidationResult validationResult => validationResult.IsValid,
            _ => true
        };
        return new { requestId, success, result };
    }

    private static async Task<object> ExecuteRequestAsync(
        string projectPath,
        IEditorSession session,
        IEditorCommandCatalog catalog,
        IProjectFileCommandCatalog fileCatalog,
        IProjectFileCommandExecutor fileExecutor,
        JsonElement request)
    {
        var elements = CommandElements(request);
        if (!elements.Any(element => IsFileCommand(element, fileCatalog)))
            return ExecuteRequest(session, catalog, request);
        if (elements.Count != 1 || !IsFileCommand(elements[0], fileCatalog))
            return FileCommandResult.Failure(EditorDiagnostic.Error("file.transaction.mixed", "File commands cannot be mixed with document commands in one transaction."));
        var id = elements[0].GetProperty("commandId").GetString()!;
        return await fileExecutor.ExecuteAsync(projectPath, fileCatalog.Deserialize(id, elements[0], JsonOptions));
    }

    private static CommandResult ExecuteRequest(
        IEditorSession session,
        IEditorCommandCatalog catalog,
        JsonElement request)
    {
        var commands = DeserializeCommands(request, catalog);
        var dryRun = request.TryGetProperty("dryRun", out var dryRunElement) && dryRunElement.GetBoolean();
        long? expectedRevision = request.TryGetProperty("expectedRevision", out var revisionElement)
            ? revisionElement.GetInt64()
            : null;
        return session.ExecuteTransaction(commands, new ExecuteOptions(expectedRevision, dryRun));
    }

    private static async Task<object> SaveAndReturnAsync(IEditorSession session)
    {
        await session.SaveAsync();
        return new { session.Revision, session.IsDirty };
    }

    private static object GetNode(IEditorSession session, JsonElement request)
    {
        var nodeId = request.GetProperty("nodeId").GetString() ?? throw new JsonException("nodeId is required.");
        var node = session.Document.Graph.Nodes.FirstOrDefault(item => item.Id == nodeId)
            ?? throw new KeyNotFoundException($"Node '{nodeId}' does not exist.");
        return new
        {
            node,
            incomingEdges = session.Document.Graph.Edges.Where(edge => edge.ToNodeId == node.Id).ToList(),
            outgoingEdges = session.Document.Graph.Edges.Where(edge => edge.FromNodeId == node.Id).ToList(),
            entries = session.Document.GroupEntries.GetValueOrDefault(node.Id)
        };
    }

    private static IReadOnlyList<EditorEntryData> GetEntries(IEditorSession session, JsonElement request)
    {
        var groupId = request.GetProperty("groupId").GetString() ?? throw new JsonException("groupId is required.");
        return session.Document.GroupEntries.TryGetValue(groupId, out var entries)
            ? entries
            : throw new KeyNotFoundException($"Group '{groupId}' does not exist or has no entry document.");
    }

    private static object Paginate<T>(IReadOnlyList<T> items, JsonElement request)
    {
        var offset = request.TryGetProperty("offset", out var offsetElement) ? Math.Max(0, offsetElement.GetInt32()) : 0;
        var requestedLimit = request.TryGetProperty("limit", out var limitElement) ? limitElement.GetInt32() : 100;
        var limit = Math.Clamp(requestedLimit, 1, 500);
        var page = items.Skip(offset).Take(limit).Select(item => SelectFields(item, request)).ToList();
        return new { items = page, offset, limit, total = items.Count, hasMore = offset + page.Count < items.Count };
    }

    private static object SelectFields<T>(T item, JsonElement request)
    {
        if (!request.TryGetProperty("fields", out var fieldsElement) || fieldsElement.ValueKind != JsonValueKind.Array)
            return item!;
        var fields = fieldsElement.EnumerateArray().Select(value => value.GetString()).Where(value => !string.IsNullOrWhiteSpace(value)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var element = JsonSerializer.SerializeToElement(item, JsonLinesOptions);
        if (element.ValueKind != JsonValueKind.Object) return item!;
        return element.EnumerateObject()
            .Where(property => fields.Contains(property.Name))
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.OrdinalIgnoreCase);
    }

    private static object GetEntry(IEditorSession session, JsonElement request)
    {
        var entryId = request.GetProperty("entryId").GetString() ?? throw new JsonException("entryId is required.");
        return GetEntries(session, request).FirstOrDefault(entry => entry.StableId == entryId)
            ?? throw new KeyNotFoundException($"Entry '{entryId}' does not exist in the requested group.");
    }

    private static object ListVariables(IEditorSession session, JsonElement request)
    {
        var scope = request.TryGetProperty("scope", out var scopeElement) ? scopeElement.GetString() : null;
        return scope?.ToLowerInvariant() switch
        {
            "player" => Paginate(session.Document.Graph.PlayerVariables, request),
            "save" => Paginate(session.Document.Graph.SaveVariables, request),
            null or "" => new
            {
                player = Paginate(session.Document.Graph.PlayerVariables, request),
                save = Paginate(session.Document.Graph.SaveVariables, request)
            },
            _ => throw new JsonException("scope must be 'player' or 'save'.")
        };
    }

    private static object GetNeighbors(IEditorSession session, JsonElement request)
    {
        var nodeId = request.GetProperty("nodeId").GetString() ?? throw new JsonException("nodeId is required.");
        if (!session.Document.Graph.Nodes.Any(node => node.Id == nodeId))
            throw new KeyNotFoundException($"Node '{nodeId}' does not exist.");
        return new
        {
            incoming = session.Document.Graph.Edges.Where(edge => edge.ToNodeId == nodeId).ToList(),
            outgoing = session.Document.Graph.Edges.Where(edge => edge.FromNodeId == nodeId).ToList()
        };
    }

    private static object FindPath(IEditorSession session, JsonElement request)
    {
        var from = request.GetProperty("fromNodeId").GetString() ?? throw new JsonException("fromNodeId is required.");
        var to = request.GetProperty("toNodeId").GetString() ?? throw new JsonException("toNodeId is required.");
        var previous = new Dictionary<string, string?>(StringComparer.Ordinal) { [from] = null };
        var queue = new Queue<string>();
        queue.Enqueue(from);
        while (queue.Count > 0 && !previous.ContainsKey(to))
        {
            var current = queue.Dequeue();
            foreach (var next in session.Document.Graph.Edges.Where(edge => edge.FromNodeId == current).Select(edge => edge.ToNodeId))
            {
                if (previous.ContainsKey(next)) continue;
                previous[next] = current;
                queue.Enqueue(next);
            }
        }
        if (!previous.ContainsKey(to)) return new { found = false, nodes = Array.Empty<string>() };
        var path = new List<string>();
        for (string? current = to; current is not null; current = previous[current]) path.Add(current);
        path.Reverse();
        return new { found = true, nodes = path.ToArray() };
    }

    private static IReadOnlyList<object> FindVariableReferences(IEditorSession session, JsonElement request)
    {
        var name = request.GetProperty("name").GetString() ?? throw new JsonException("name is required.");
        var references = new List<object>();
        foreach (var (groupId, entries) in session.Document.GroupEntries)
        foreach (var entry in entries)
        {
            if (ContainsWord(entry.Condition, name) || ContainsWord(entry.Parameters, name))
                references.Add(new { resource = $"groups/{groupId}/entries/{entry.StableId}", entry.Condition, entry.Parameters });
        }
        foreach (var node in session.Document.Graph.Nodes)
        {
            foreach (var option in node.Options ?? [])
                if (ContainsWord(option.Condition, name)) references.Add(new { resource = $"graph/nodes/{node.Id}/options/{option.Id}", expression = option.Condition });
            foreach (var condition in node.Conditions ?? [])
                if (ContainsWord(condition.Expression, name)) references.Add(new { resource = $"graph/nodes/{node.Id}/conditions/{condition.Id}", expression = condition.Expression });
        }
        return references;
    }

    private static bool ContainsWord(string value, string word) =>
        System.Text.RegularExpressions.Regex.IsMatch(value ?? "", $@"\b{System.Text.RegularExpressions.Regex.Escape(word)}\b");

    private static object SearchAssets(string projectPath, JsonElement request)
    {
        var query = request.TryGetProperty("query", out var queryElement) ? queryElement.GetString() ?? "" : "";
        var assetsPath = Path.Combine(projectPath, "Assets");
        if (!Directory.Exists(assetsPath)) return Paginate(Array.Empty<object>(), request);
        var items = Directory.EnumerateFiles(assetsPath, "*", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(assetsPath, path).Replace('\\', '/'))
            .Where(path => path.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(path => new { path })
            .ToList();
        return Paginate(items, request);
    }

    private static object CreateSummary(string projectPath, IEditorSession session) => new
    {
        project = new
        {
            name = session.Document.Graph.Name,
            path = projectPath,
            revision = session.Revision,
            isDirty = session.IsDirty,
            canUndo = session.CanUndo,
            canRedo = session.CanRedo
        },
        graph = new
        {
            nodeCount = session.Document.Graph.Nodes.Count,
            edgeCount = session.Document.Graph.Edges.Count,
            entryCount = session.Document.GroupEntries.Values.Sum(entries => entries.Count),
            rootNodeId = session.Document.Graph.RootNodeId
        },
        variables = new
        {
            player = session.Document.Graph.PlayerVariables.Count,
            save = session.Document.Graph.SaveVariables.Count
        },
        diagnostics = new
        {
            errors = session.Validate().Diagnostics.Count(item => item.Severity == EditorDiagnosticSeverity.Error),
            warnings = session.Validate().Diagnostics.Count(item => item.Severity == EditorDiagnosticSeverity.Warning)
        }
    };

    private static object NodeSummary(EditorGraphNodeDto node) => new
    {
        node.Id,
        node.Type,
        node.BranchType,
        node.Name,
        node.X,
        node.Y,
        optionCount = node.Options?.Count ?? 0,
        conditionCount = node.Conditions?.Count ?? 0
    };

    private static void WriteJson(object value) =>
        Console.Out.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

    private static int Fail(string message, int code = 2)
    {
        Console.Error.WriteLine(message);
        return code;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            GalNet Editor Headless

            Usage:
              galnet-editor-headless commands
              galnet-editor-headless create <project> [--name <name>]
              galnet-editor-headless summary <project>
              galnet-editor-headless nodes <project>
              galnet-editor-headless node <project> <node-id>
              galnet-editor-headless entries <project> <group-id>
              galnet-editor-headless variables <project>
              galnet-editor-headless validate <project>
              galnet-editor-headless execute <project> <command-file|-> [--no-save]
              galnet-editor-headless serve <project>

            The serve command reads one JSON request per stdin line and preserves revision and Undo/Redo history.
            """);
    }
}
