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



            if (args[0].Equals("project.create", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length < 2) return Fail("create requires a project path.");
                var nameIndex = Array.IndexOf(args, "--name");
                var name = nameIndex >= 0 && nameIndex + 1 < args.Length ? args[nameIndex + 1] : null;
                await EditorProjectCreator.CreateAsync(args[1], new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules()), name);
                WriteJson(new { success = true, projectPath = Path.GetFullPath(args[1]), name });
                return 0;
            }
            if (args.Length < 2) return Fail("A project path is required.");

            var verb = args[0].ToLowerInvariant();
            var projectPath = Path.GetFullPath(args[1]);
            if (RunQuery(projectPath, invocation) is { } queryResult)
            {
                WriteJson(queryResult);
                return queryResult.Success ? 0 : queryResult.Diagnostics.Any(item => item.Code == "object.notFound") ? 3 : 4;
            }
            if (_operationId is "schema.list" or "schema.show")
            {
                using var schema = ProjectSchemaContext.Load(projectPath);
                var result = ProjectSchemaQueryService.Describe(schema,
                    _operationId == "schema.show" ? invocation.Positionals[1] : null,
                    invocation.Get("--kind"), invocation.Get("--category"),
                    NonnegativeInteger(invocation, "--offset", 0),
                    invocation.Has("--limit") ? NonnegativeInteger(invocation, "--limit", 0) : null);
                WriteJson(result);
                return result.Success ? 0 : 3;
            }
            if (_operationId is "project.validate" or "graph.validate" or "group.validate")
            {
                var scope = _operationId.Split('.')[0];
                var result = new ProjectValidationService(projectPath, scope == "graph" ? invocation.Get("--file") ?? "Graph/graph.json" : "Graph/graph.json").Validate(scope,
                    new GroupSelector(invocation.Get("--id"), invocation.Get("--file")), invocation.Has("--structure-only"));
                WriteJson(result);
                return result.Success ? 0 : result.Diagnostics.Any(item => item.Code == "contract.load") ? 1 : 4;
            }
            if (_operationId == "group.compile")
            {
                var result = await new GroupCompileService(projectPath).CompileAsync(new(
                    new GroupSelector(invocation.Get("--id"), invocation.Get("--file")), invocation.Get("--output"), invocation.Has("--dry-run")));
                WriteJson(result);
                return result.Success ? 0 : result.Diagnostics.Any(item => item.Code == "contract.load") ? 1 : result.Diagnostics.Any(item => item.Code == "object.notFound") ? 3 : 5;
            }
            using var projectSchema = ProjectSchemaContext.Load(projectPath);
            var entryCatalog = projectSchema.Catalog;
            if (verb == "project.build") return await BuildAsync(projectPath, args, entryCatalog);
            if (verb == "project.export") return await ExportAsync(projectPath, args, entryCatalog);
            var persistence = new DirectProjectPersistence(projectPath, new EditorDocumentRepository(entryCatalog));
            var document = persistence.Load();
            switch (verb)
            {
                case "execute":
                    if (args.Length < 3) return Fail("execute requires a JSON file path or '-'.");
                    var json = args[2] == "-" ? await Console.In.ReadToEndAsync() : await File.ReadAllTextAsync(args[2]);
                    return await ExecuteAsync(projectPath, document, new EditorCommandCatalog(), new AssetFileCommandCatalog(), entryCatalog, persistence, json);
                default: return Fail($"Unknown command '{args[0]}'.");
            }
        }
        catch (ArgumentException exception) { return Fail(exception.Message); }
        catch (JsonException exception) { return Fail($"Invalid JSON: {exception.Message}"); }
        catch (Exception exception) { return Fail(exception.Message, 1); }
    }

    private static async Task<int> ExecuteAsync(string projectPath, EditorProjectDocument original,
        EditorCommandCatalog catalog, AssetFileCommandCatalog fileCatalog, IEntryCatalog entryCatalog, DirectProjectPersistence persistence, string json)
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
        _ = persistence.PrepareSave(working);
        if (!dryRun) await persistence.SaveAsync(working);
        WriteJson(new { success = true, dryRun, description = string.Join("; ", descriptions), changedResources = changed.Distinct(), diagnostics = validation.Diagnostics });
        return 0;
    }

    private static async Task<int> ExportAsync(string projectPath, string[] args, IEntryCatalog entryCatalog)
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
        var result = await GamePackageExporter.ExportAsync(projectName, projectName, projectPath, outputDirectory, resourceTypes, galleryTypes, entryCatalog);
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

    private static ToolResult? RunQuery(string project, CliInvocation invocation)
    {
        var id = invocation.Definition.OperationId;
        if (id is not ("project.info" or "files" or "find" or "references" or "graph.show" or "graph.nodes" or "graph.edges" or "graph.node" or "group.list" or "group.show" or "group.entries" or "variables")) return null;
        var service = new ProjectQueryService(project);
        var offset = NonnegativeInteger(invocation, "--offset", 0);
        int? limit = invocation.Has("--limit") ? NonnegativeInteger(invocation, "--limit", 0) : null;
        var selector = new GroupSelector(invocation.Get("--id"), invocation.Get("--file"));
        return id switch
        {
            "project.info" => service.Info(),
            "files" => service.Files(new(invocation.Get("--root"), invocation.Get("--glob"), invocation.Get("--extension"), invocation.Get("--kind"), offset, limit)),
            "find" => service.Find(new(invocation.Positionals[1], invocation.Get("--kind"), invocation.Get("--group"), offset, limit)),
            "references" => service.References(invocation.Positionals[1], invocation.Get("--kind")!, offset, limit),
            "graph.show" or "graph.nodes" or "graph.edges" => service.Graph(id, type: invocation.Get("--type"), offset: offset, limit: limit),
            "graph.node" => service.Graph(id, id: invocation.Positionals[1]),
            "group.list" => service.Groups(offset, limit),
            "group.show" => service.Group(selector, entries: false),
            "group.entries" => service.Group(selector, entries: true, invocation.Get("--type"), invocation.Get("--entry-id"), offset, limit),
            "variables" => service.Variables(invocation.Get("--scope"), invocation.Get("--name"), offset, limit),
            _ => null
        };
    }

    private static int NonnegativeInteger(CliInvocation invocation, string key, int fallback)
    {
        var text = invocation.Get(key);
        if (text is null) return fallback;
        if (!int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value))
            throw new ArgumentException($"{key} must be a nonnegative integer.");
        return value;
    }


    private static void WriteJson(object value)
    {
        if (value is ToolResult result)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }
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
