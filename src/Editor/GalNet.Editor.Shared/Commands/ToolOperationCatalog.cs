using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;
using GalNet.Editor.Abstraction.Commands;

namespace GalNet.Editor.Shared.Commands;

/// <summary>Discoverable routes and typed operations; only available operations are advertised.</summary>
public sealed class ToolOperationCatalog
{
    public static readonly IReadOnlyDictionary<int, string> ExitCodes = new Dictionary<int, string>
    {
        [0] = "Success", [1] = "I/O, contract loading or unexpected failure",
        [2] = "Invalid command or arguments", [3] = "Object not found",
        [4] = "Validation failed", [5] = "Compilation, export or edit failed"
    };

    private static readonly JsonElement ResultSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        required = new[] { "protocolVersion", "operationId", "success", "data", "diagnostics" },
        properties = new
        {
            protocolVersion = new { type = "integer", @const = 1 }, operationId = new { type = "string" },
            success = new { type = "boolean" }, data = new { },
            diagnostics = new { type = "array", items = new { type = "object", required = new[] { "code", "severity", "message" } } }
        }
    });

    public ToolOperationCatalog()
    {
        var definitions = new List<ToolOperationDefinition>
        {
            Route("help", "help", "Lists available commands or describes one command or typed operation.",
                [new("command", "string", "Command path or operation ID.", Positional: true),
                 new("--json", "boolean", "Return the complete machine contract.", Default: false),
                 new("--search", "string", "Search operation ID, command path and description.")],
                requiresProject: false),
            Route("project.info", "project info", "Shows raw project settings, graph and Group counts without modifying files.", Project()),
            Route("project.create", "project create", "Creates an empty author project, optionally deploying a validated contract directory. Without a contract, semantic operations remain unavailable.",
                Project(new CliParameter("--name", "string", "Project display name."), new("--contracts", "string", "Built contract directory containing GalNet.Project.Contracts.dll and managed dependencies."),
                    new("--dry-run", "boolean", "Validate inputs and report files without creating directories or files.", Default: false)), readOnly: false, requiresProject: false, dryRun: true),
            Route("files", "files", "Lists author files with intersection filters (each option once). Globs support *, ** and ? against project-relative paths; explicit --root can include Contracts/Output.",
                Query(new CliParameter("--root", "string", "Relative directory to scan recursively."), new("--glob", "string", "Project-relative glob pattern."),
                    new("--extension", "string", "File extension, with or without dot."), new("--kind", "string", "File kind.", Enum: ["graph", "settings", "rawgroup", "compiledgroup", "metadata", "resource", "localization", "other"]))),
            Route("find", "find", "Returns all matching resource GUIDs, node IDs or entry IDs, including unknown entry content.",
                Query(new CliParameter("id", "string", "Stable object ID or resource GUID.", true, true),
                    new("--kind", "string", "Limit identity kind.", Enum: ["resource", "node", "entry"]), new("--group", "string", "Limit entry lookup to a Group ID."))),
            Route("references", "references", "Finds static JSON reference candidates. Reports incomplete analysis for custom C# and runtime logic.",
                Query(new CliParameter("target", "string", "Referenced ID or variable name.", true, true),
                    new("--kind", "string", "Reference identity kind.", true, Enum: ["resource", "node", "group", "entry", "variable"]))),
            Route("graph.show", "graph show", "Returns the original graph JSON including unknown fields.", Project()),
            Route("graph.nodes", "graph nodes", "Lists original graph nodes.", Query(new CliParameter("--type", "string", "Filter exact node type."))),
            Route("graph.edges", "graph edges", "Lists original graph edges.", Query()),
            Route("graph.node", "graph node", "Returns original node matches without inventing IDs or filtering fields.",
                Project(new CliParameter("node-id", "string", "Stable node ID; returns all matches if duplicated.", true, true))),
            Route("group.list", "group list", "Lists Group source files including unreferenced Raw files.", Query()),
            Route("group.show", "group show", "Returns the original Raw Group JSON. Specify exactly one of --id/--file.", Project(Selector())),
            Route("group.entries", "group entries", "Lists original Group entries. Specify exactly one of --id/--file.",
                Query(Selector().Concat([new CliParameter("--type", "string", "Filter exact entry type."), new("--entry-id", "string", "Filter exact entry ID.")]).ToArray())),
            Route("variables", "variables", "Lists original player/save variable definitions without migration.",
                Query(new CliParameter("--scope", "string", "Variable scope.", Enum: ["player", "save"]), new("--name", "string", "Filter exact variable name."))),
            Route("schema.list", "schema list", "Lists schemas declared by the project's Contracts DLL, with origin and parameter JSON descriptions.",
                Query(new CliParameter("--kind", "string", "Filter schema kind.", Enum: ["primitive", "composite"]), new("--category", "string", "Filter exact schema category.")), requiresContract: true),
            Route("schema.show", "schema show", "Describes one declared entry schema including defaults and constraints; executable validators remain in the DLL.",
                Project(new CliParameter("type-id", "string", "Full entry type ID.", true, true)), requiresContract: true),
            Route("project.validate", "validate", "Validates raw project Graph, all Raw Groups (including orphans), settings JSON and resource metadata without repair or writes. Semantic checks require the project contract; skipped checks are explicit.", Project(new CliParameter("--structure-only", "boolean", "Check raw structure without loading entry contracts.", Default: false)), requiresContract: true),
            Route("graph.validate", "graph validate", "Validates raw Graph structure, topology and variables; full mode also checks source references and condition syntax. Does not validate Group contents.", Project(new CliParameter("--file", "string", "Project-relative graph path (default Graph/graph.json)."), new("--structure-only", "boolean", "Skip source reference and condition syntax checks.", Default: false))),
            Route("group.validate", "group validate", "Validates only the selected Raw Group. Full mode validates parameters, contract validators and Composite expansion; unrelated Groups cannot block this operation.", Project(Selector().Concat([new CliParameter("--structure-only", "boolean", "Check raw structure without loading entry contracts.", Default: false)]).ToArray()), requiresContract: true),
            Route("group.compile", "group compile", "Compiles only the selected Raw Group using its project contract. A file without a unique Group owner requires explicit output; failure preserves old output.",
                Project(Selector().Concat([new CliParameter("--output", "string", "Compiled .galgroup path, relative to project or absolute. Inside project only Output/ is allowed."), new("--dry-run", "boolean", "Compile and report without publishing any file.", Default: false)]).ToArray()), readOnly: false, requiresContract: true, dryRun: true),
            Route("project.build", "build", "Compiles project content.", Project(new CliParameter("--output", "string", "Output directory (default: project/Output).")),
                readOnly: false, requiresContract: true),
            Route("project.export", "export", "Exports a project to a galpak package.", Project(new CliParameter("--output", "string", "Output directory (default: project/Output).")),
                readOnly: false, requiresContract: true),
            Route("execute", "execute", "Executes JSON typed document operations or one asset file operation.",
                Project(new CliParameter("command-file", "string", "JSON file path, or '-' to read stdin.", true, true)),
                readOnly: false, requiresContract: true, dryRun: true, batchMode: "Document operations only; one file operation per invocation. File dry-run is unsupported.")
        };
        var document = new EditorCommandCatalog();
        var files = new AssetFileCommandCatalog();
        definitions.AddRange(document.GetAll().Select(definition => FromCommand(definition, file: false)));
        definitions.AddRange(files.GetAll().Select(definition => FromCommand(definition, file: true)));
        Definitions = Array.AsReadOnly(definitions.Select(DescribeOutput).OrderBy(definition => definition.OperationId, StringComparer.Ordinal).ToArray());
    }

    public IReadOnlyList<ToolOperationDefinition> Definitions { get; }

    public IReadOnlyList<ToolOperationDefinition> Search(string? keyword) => string.IsNullOrWhiteSpace(keyword)
        ? Definitions
        : Definitions.Where(definition =>
            $"{definition.OperationId} {definition.CommandPath} {definition.Description}"
                .Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToArray();

    public ToolOperationDefinition? Find(string command) => Definitions.FirstOrDefault(definition =>
        definition.OperationId.Equals(command, StringComparison.OrdinalIgnoreCase) ||
        definition.CommandPath.Equals(command, StringComparison.OrdinalIgnoreCase));

    public (ToolOperationDefinition Definition, int Consumed) Resolve(IReadOnlyList<string> args)
    {
        var match = Definitions.Where(definition => definition.Transport == "cli")
            .Select(definition => (Definition: definition, Words: definition.CommandPath.Split(' ')))
            .Where(item => item.Words.Length <= args.Count && item.Words.Select((word, index) => word.Equals(args[index], StringComparison.OrdinalIgnoreCase)).All(value => value))
            .OrderByDescending(item => item.Words.Length).FirstOrDefault();
        return match.Definition is null
            ? throw new ArgumentException($"Unknown command '{string.Join(' ', args)}'. Use help --json to discover commands.")
            : (match.Definition, match.Words.Length);
    }

    public static IReadOnlyList<CliParameter> Project(params CliParameter[] extra) =>
        new CliParameter[] { new("project", "string", "Explicit project directory.", true, true) }.Concat(extra).ToArray();

    public static IReadOnlyList<CliParameter> Query(params CliParameter[] extra) => Project(extra.Concat(
        [new CliParameter("--offset", "integer", "Nonnegative result offset.", Default: 0), new("--limit", "integer", "Nonnegative page size; absent means all results.")]).ToArray());

    public static CliParameter[] Selector() =>
        [new("--id", "string", "Unique Group node ID; mutually exclusive with --file."), new("--file", "string", "Project-relative Raw Group source path; mutually exclusive with --id.")];

    private static ToolOperationDefinition DescribeOutput(ToolOperationDefinition operation)
    {
        Type? dataType = operation.OperationId switch
        {
            "project.create" => typeof(ProjectCreateReport),
            "files" => typeof(QueryPage<ProjectFileItem>),
            "find" or "graph.nodes" or "graph.node" or "graph.edges" or "group.entries" or "variables" => typeof(QueryPage<ProjectObjectItem>),
            "group.list" => typeof(QueryPage<ProjectGroupItem>),
            "references" => typeof(QueryPage<ProjectReferenceItem>),
            "graph.show" or "group.show" => typeof(RawDocumentQuery),
            "project.validate" or "graph.validate" or "group.validate" => typeof(ProjectValidationReport),
            "group.compile" => typeof(GroupCompileReport),
            _ => null
        };
        if (dataType is null) return operation;
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        var result = JsonNode.Parse(ResultSchema.GetRawText())!;
        result["properties"]!["data"] = options.GetJsonSchemaAsNode(dataType);
        return operation with { OutputSchema = JsonSerializer.SerializeToElement(result) };
    }

    private static ToolOperationDefinition FromCommand(IProjectCommandDefinition definition, bool file) => new(
        definition.Id, "execute", definition.Description, "execute <project> <command-file|->",
        definition.Schema.Parameters.Select(parameter => new CliParameter(parameter.Name, parameter.Type, parameter.Description, parameter.Required,
            Default: parameter.JsonSchema!.Value.TryGetProperty("default", out var defaultValue) ? defaultValue : null,
            Enum: parameter.JsonSchema.Value.TryGetProperty("enum", out var options)
                ? options.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).ToArray() : null,
            Schema: parameter.JsonSchema)).ToArray(),
        definition.Schema.JsonSchema!.Value, ResultSchema, false, true, true, !file,
        file ? "Single file operation; cannot mix with document operations." : "Document-only batch; dryRun is set on the enclosing JSON payload.",
        [$"execute <project> <file containing commandId='{definition.Id}'>"], ExitCodes,
        file ? "file-operation" : "document-operation");

    public static ToolOperationDefinition Route(string id, string path, string description, IReadOnlyList<CliParameter> parameters,
        bool readOnly = true, bool requiresProject = true, bool requiresContract = false,
        bool dryRun = false, string batchMode = "None")
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var parameter in parameters)
        {
            var property = new JsonObject { ["type"] = parameter.Type, ["description"] = parameter.Description };
            if (parameter.Default is not null) property["default"] = JsonSerializer.SerializeToNode(parameter.Default);
            if (parameter.Enum is not null) property["enum"] = JsonSerializer.SerializeToNode(parameter.Enum);
            properties[parameter.Name] = property;
            if (parameter.Required) required.Add(parameter.Name);
        }
        var inputSchema = new JsonObject
        { ["type"] = "object", ["properties"] = properties, ["required"] = required, ["additionalProperties"] = false };
        if (properties.ContainsKey("--id") && properties.ContainsKey("--file"))
            inputSchema["oneOf"] = new JsonArray(
                new JsonObject { ["required"] = new JsonArray("--id") },
                new JsonObject { ["required"] = new JsonArray("--file") });
        var input = JsonSerializer.SerializeToElement(inputSchema);
        var usage = path + string.Concat(parameters.Select(parameter => parameter.Positional
            ? (parameter.Required ? $" <{parameter.Name}>" : $" [<{parameter.Name}>]")
            : parameter.Type == "boolean" ? $" [{parameter.Name}]" : $" [{parameter.Name} <value>]"));
        return new(id, path, description, usage, parameters, input, ResultSchema,
            readOnly, requiresProject, requiresContract, dryRun, batchMode, [usage], ExitCodes);
    }
}
