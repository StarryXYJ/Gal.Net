using System.Text.Json;
using System.Text.Json.Nodes;
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
            Route("project.info", "project info", "Shows project and graph counts and validation summary.", Project()),
            Route("project.create", "project create", "Creates an editable project using the current built-in starter content.",
                Project(new CliParameter("--name", "string", "Project display name.")), readOnly: false),
            Route("graph.nodes", "graph nodes", "Lists graph nodes.", Project()),
            Route("graph.node", "graph node", "Shows a node with connections and group entries.",
                Project(new CliParameter("node-id", "string", "Stable node ID.", true, true))),
            Route("group.entries", "group entries", "Lists entries of a group.",
                Project(new CliParameter("group-id", "string", "Stable Group node ID.", true, true)), requiresContract: true),
            Route("variables", "variables", "Lists player and save variable definitions.", Project()),
            Route("project.validate", "validate", "Validates the loaded editable project document (raw disk validation will follow).", Project(), requiresContract: true),
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
        Definitions = Array.AsReadOnly(definitions.OrderBy(definition => definition.OperationId, StringComparer.Ordinal).ToArray());
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
        var input = JsonSerializer.SerializeToElement(new JsonObject
        { ["type"] = "object", ["properties"] = properties, ["required"] = required, ["additionalProperties"] = false });
        var usage = path + string.Concat(parameters.Select(parameter => parameter.Positional
            ? (parameter.Required ? $" <{parameter.Name}>" : $" [<{parameter.Name}>]")
            : parameter.Type == "boolean" ? $" [{parameter.Name}]" : $" [{parameter.Name} <value>]"));
        return new(id, path, description, usage, parameters, input, ResultSchema,
            readOnly, requiresProject, requiresContract, dryRun, batchMode, [usage], ExitCodes);
    }
}
