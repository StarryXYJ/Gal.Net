using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Core.Assets;
using GalNet.Core.Entry;
using GalNet.Core.Gallery;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Shared.Commands;
using GalNet.Editor.Shared.Services;
using GalNet.ProjectTools.Windows;

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
                var result = await EditorProjectCreator.CreateAsync(new(invocation.Positionals[0], invocation.Get("--name"),
                    invocation.Get("--contracts"), invocation.Has("--dry-run")));
                WriteJson(result);
                return 0;
            }
            if (args.Length < 2) return Fail("A project path is required.");

            var verb = args[0].ToLowerInvariant();
            var projectPath = Path.GetFullPath(args[1]);
            if (_operationId == "project.delete")
            {
                var result = await new ProjectDeleteService(new WindowsProjectRecycleBin()).DeleteAsync(projectPath, invocation.Has("--dry-run"));
                WriteJson(result);
                return result.Success ? 0 : 5;
            }
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
                    invocation.GetValue("--offset")?.GetInt32() ?? 0,
                    invocation.GetValue("--limit")?.GetInt32());
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
            var edits = new ProjectEditService(projectPath, entryCatalog);
            if (verb is "project.rename" or "group.create" or "group.rename" or "group.delete")
            {
                IProjectEditCommand command = verb switch
                {
                    "project.rename" => new RenameProjectCommand(invocation.Get("--name")!),
                    "group.create" => new CreateNodeCommand(invocation.Get("--id") ?? Guid.NewGuid().ToString("N"), EditorNodeKind.LinearGroup,
                        invocation.Get("--name")!, Coordinate(invocation, "--x"), Coordinate(invocation, "--y")),
                    "group.rename" => new RenameNodeCommand(invocation.Get("--id")!, invocation.Get("--name")!),
                    _ => new DeleteNodeCommand(invocation.Get("--id")!, ForceReferences: invocation.Has("--force"))
                };
                var result = verb == "project.rename"
                    ? await edits.ExecuteAsync([command], invocation.Has("--dry-run"), verb)
                    : await edits.ExecuteGroupAsync(command, invocation.Has("--dry-run"));
                WriteJson(result);
                return result.Success ? 0 : 5;
            }
            switch (verb)
            {
                case "execute":
                    if (args.Length < 3) return Fail("execute requires a JSON file path or '-'.");
                    var json = args[2] == "-" ? await Console.In.ReadToEndAsync() : await File.ReadAllTextAsync(args[2]);
                    var result = await edits.ExecuteJsonAsync(json);
                    WriteJson(result);
                    return result.Success ? 0 : result.Diagnostics.Any(item => item.Code is "command.arguments" or "json.propertyDuplicate") ? 2 : 5;
                default: return Fail($"Unknown command '{args[0]}'.");
            }
        }
        catch (ArgumentException exception) { return Fail(exception.Message); }
        catch (JsonException exception) { return Fail($"Invalid JSON: {exception.Message}"); }
        catch (Exception exception) { return Fail(exception.Message, 1); }
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
        WriteJson(new ToolResult("project.export", result.Success, new ProjectExportReport(result.PackagePath),
            result.Success ? [] : [new("project.exportFailed", "error", result.Error ?? "Export failed.")]));
        return result.Success ? 0 : 5;
    }

    private static async Task<int> BuildAsync(string projectPath, string[] args, IEntryCatalog entryCatalog)
    {
        if (!Directory.Exists(projectPath)) throw new DirectoryNotFoundException($"Project directory not found: {projectPath}");
        var outputIndex = Array.IndexOf(args, "--output");
        if (outputIndex >= 0 && outputIndex + 1 >= args.Length) return Fail("--output requires a directory path.");
        var outputDirectory = outputIndex >= 0 ? Path.GetFullPath(args[outputIndex + 1]) : null;
        var result = await new ProjectContentBuilder(entryCatalog).BuildAsync(projectPath, outputDirectory);
        WriteJson(new ToolResult("project.build", true, new ProjectBuildReport(result.OutputPath, result.CompiledGroupCount), []));
        return 0;
    }

    private static ToolResult? RunQuery(string project, CliInvocation invocation)
    {
        var id = invocation.Definition.OperationId;
        if (id is not ("project.info" or "files" or "find" or "references" or "graph.show" or "graph.nodes" or "graph.edges" or "graph.node" or "group.list" or "group.show" or "group.entries" or "variables")) return null;
        var service = new ProjectQueryService(project);
        var offset = invocation.GetValue("--offset")?.GetInt32() ?? 0;
        int? limit = invocation.GetValue("--limit")?.GetInt32();
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

    private static double Coordinate(CliInvocation invocation, string key)
    {
        var text = invocation.Get(key);
        if (text is null) return 0;
        if (!double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
            throw new ArgumentException($"{key} must be a finite number.");
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
