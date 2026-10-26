using GalNet.Contracts.Builtins.Assets;
using System.Text.Json;
using GalNet.Core.Assets;
using GalNet.Core.Compilation;
using GalNet.Core.Entry;
using GalNet.Core.Configuration;
using GalNet.Core.Serialization;
using GalNet.Core.Variable;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Shared.Commands;

namespace GalNet.Editor.Shared.Services;

/// <summary>Validates author files without repository repair, migration or persistence.</summary>
public sealed class ProjectValidationService(string projectDirectory, string graphFile = "Graph/graph.json", IResourceTypeCatalog? resourceTypes = null)
{
    private readonly string GraphFile = graphFile.Replace('\\', '/');
    private readonly ProjectRawReader _reader = new(projectDirectory);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ToolResult Validate(string scope = "project", GroupSelector? selector = null, bool structureOnly = false,
        IEntryCatalog? catalog = null, string? contractSha256 = null, ProjectConfigurationSchema? configurations = null)
    {
        if (scope is not ("project" or "graph" or "group")) throw new ArgumentException("Unknown validation scope.");
        _ = _reader.Resolve(GraphFile);
        var diagnostics = new List<ToolDiagnostic>();
        var files = new List<string>();
        var groups = new List<string>();
        if (scope == "project") diagnostics.AddRange(ImageVariantsValidation.Validate(_reader.Root, resourceTypes ?? BuiltinResourceTypes.CreateCatalog()));
        if (scope == "group")
        {
            var file = new ProjectQueryService(_reader.Root).ResolveGroup(selector ?? throw new ArgumentException("Group selector required."), diagnostics);
            if (file is not null) groups.Add(file);
        }
        else
        {
            files.Add(GraphFile);
            ValidateGraph(_reader.Read(GraphFile, diagnostics), diagnostics, structureOnly);
            if (scope == "project")
            {
                var authorFiles = _reader.Files(null, diagnostics);
                groups.AddRange(authorFiles.Where(file => file.EndsWith(".rawgalgroup", StringComparison.OrdinalIgnoreCase)));
                var resourceIds = new HashSet<Guid>();
                foreach (var file in authorFiles.Where(file => file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)))
                {
                    files.Add(file);
                    var meta = _reader.Read(file, diagnostics);
                    if (meta is null) continue;
                    DuplicateProperties(meta.Value, file, "", diagnostics);
                    var identity = ProjectRawReader.MetadataIdentity(meta.Value);
                    if (!Guid.TryParse(identity.Id, out var guid))
                        Error(diagnostics, "asset.guidInvalid", "Metadata must contain one string resource GUID id.", file, identity.Pointer);
                    else if (!resourceIds.Add(guid)) Error(diagnostics, "asset.guidDuplicate", "Resource GUID is duplicated.", file, identity.Pointer, guid.ToString());
                    if (!File.Exists(_reader.Resolve(file[..^5]))) Error(diagnostics, "asset.fileMissing", "Metadata has no adjacent resource file.", file, "");
                }
                files.Add("settings.json");
                if (_reader.Read("settings.json", diagnostics) is { } settings)
                {
                    Object(settings, "settings.json", "", diagnostics);
                    DuplicateProperties(settings, "settings.json", "", diagnostics);
                }
            }
        }

        ProjectSchemaContext? context = null;
        try
        {
            if (!structureOnly && scope != "graph" && catalog is null)
            {
                try { context = ProjectSchemaContext.Load(_reader.Root); catalog = context.Catalog; contractSha256 = context.Fingerprint; configurations = context.Configurations; }
                catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
                { Error(diagnostics, "contract.load", exception.Message, "Contracts/" + ProjectSchemaContext.AssemblyFileName, ""); }
            }
            if (scope == "project")
            {
                try
                {
                    var configuration = DirectProjectPersistence.LoadSettings(_reader.Root);
                    if (!structureOnly) configurations?.Validate(configuration);
                }
                catch (Exception error) when (error is IOException or JsonException or InvalidDataException or ArgumentException)
                { Error(diagnostics, "configuration.invalid", error.Message, "settings.json", ""); }
            }
            foreach (var file in groups.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                files.Add(file);
                ValidateGroup(file, diagnostics, structureOnly ? null : catalog, structureOnly);
            }
            if (!structureOnly && scope == "project" && catalog is not null)
            {
                diagnostics.AddRange(LocalizationValidation.ValidateDisk(_reader.Root, catalog));
                if (Directory.Exists(_reader.Resolve("I18n"))) files.AddRange(_reader.Files("I18n", diagnostics).Where(file => file.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && !file.StartsWith("I18n/External/", StringComparison.OrdinalIgnoreCase)));
                if (File.Exists(_reader.Resolve(LocalizationJson.MetadataPath))) files.Add(LocalizationJson.MetadataPath);
            }
        }
        finally { context?.Dispose(); }

        var semantic = structureOnly ? "skipped" : scope == "graph" || catalog is not null ? "checked" : "unavailable";
        var checks = new List<string> { "raw-json", "duplicate-properties", "current-format", "stable-ids" };
        if (scope != "group") checks.AddRange(["graph-topology", "variable-definitions"]);
        if (scope != "group" && !structureOnly) checks.Add("graph-source-references");
        if (scope == "project") checks.AddRange(["settings-json", "metadata-guid-and-adjacent-file", "image-variants"]);
        if (!structureOnly) checks.Add("condition-syntax");
        if (!structureOnly && scope != "graph" && catalog is not null) checks.AddRange(["entry-parameters-and-contract-validator", "composite-compilation"]);
        var skipped = new List<string> { "runtime-expression-evaluation", "custom-code-references", "asset-payloads" };
        if (scope == "project" && !structureOnly && configurations is not null) checks.Add("configuration-schema");
        else skipped.Add("configuration-schema");
        if (!structureOnly && scope == "project" && catalog is not null) checks.Add("localization-semantics");
        else skipped.Add("localization-semantics");
        if (structureOnly) skipped.AddRange(["condition-syntax", "entry-contracts", "graph-source-references"]);
        else if (scope != "graph" && catalog is null) skipped.Add("entry-contracts");
        return new(scope + ".validate", !diagnostics.Any(item => item.Severity == "error"),
            new ProjectValidationReport(scope, structureOnly, semantic, files.Distinct().Order(StringComparer.Ordinal).ToArray(), checks, skipped,
                semantic != "unavailable" && !diagnostics.Any(item => item.Code is "file.read" or "directory.read" or "file.linkSkipped" or "json.invalid" or "json.objectRequired" or "graph.shapeInvalid" or "localization.incomplete" or "localization.invalid"), contractSha256), diagnostics);
    }

    /// <summary>Checks a selected source and returns a DTO only after its raw structure is accepted.</summary>
    public GroupDocument? ValidateGroup(string file, IList<ToolDiagnostic> diagnostics, IEntryCatalog? catalog = null, bool structureOnly = false)
        => ValidateGroup(file, diagnostics, out _, catalog, structureOnly);

    internal GroupDocument? ValidateGroup(string file, IList<ToolDiagnostic> diagnostics, out CompiledGroupResult? compilation,
        IEntryCatalog? catalog = null, bool structureOnly = false)
    {
        compilation = null;
        var content = _reader.Read(file, diagnostics);
        return content is null ? null : ValidateGroupDocument(content.Value, file, diagnostics, out compilation, catalog, structureOnly);
    }

    public static GroupDocument? ValidateGroupDocument(JsonElement raw, string file, IList<ToolDiagnostic> diagnostics, IEntryCatalog? catalog = null, bool structureOnly = false)
        => ValidateGroupDocument(raw, file, diagnostics, out _, catalog, structureOnly);

    private static GroupDocument? ValidateGroupDocument(JsonElement raw, string file, IList<ToolDiagnostic> diagnostics,
        out CompiledGroupResult? compilation, IEntryCatalog? catalog, bool structureOnly)
    {
        compilation = null;
        var start = diagnostics.Count;
        if (!Shape(raw, file, "", diagnostics, ["version", "kind", "entries"])) return null;
        DuplicateProperties(raw, file, "", diagnostics);
        Version(raw, GroupDocument.CurrentVersion, file, diagnostics);
        if (ProjectRawReader.Text(raw, "kind") != "Raw") Error(diagnostics, "group.kindInvalid", "Only current Raw source documents are accepted.", file, "/kind");
        var entries = ProjectRawReader.Array(raw, "entries", file, diagnostics);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i]; var pointer = $"/entries/{i}";
            if (!Shape(entry, file, pointer, diagnostics, ["id", "type", "condition", "parameters"])) continue;
            Identity(entry, "id", ids, file, pointer, diagnostics);
            String(entry, "type", file, pointer, diagnostics, required: true);
            String(entry, "condition", file, pointer, diagnostics);
            if (!entry.TryGetProperty("parameters", out var parameters) || !Object(parameters, file, pointer + "/parameters", diagnostics))
            { Error(diagnostics, "entry.parametersRequired", "Entry parameters must be an object.", file, pointer + "/parameters"); continue; }
            if (!structureOnly) Condition(ProjectRawReader.Text(entry, "condition"), file, pointer + "/condition", diagnostics);
            if (structureOnly || catalog is null) continue;
            var type = ProjectRawReader.Text(entry, "type");
            if (type is null || !catalog.TryGet(type, out var definition))
            { Error(diagnostics, "entry.typeUnknown", $"Entry '{type}' is not declared by the project contract.", file, pointer + "/type", ProjectRawReader.Text(entry, "id")); continue; }
        }
        if (diagnostics.Skip(start).Any(item => item.Severity == "error")) return null;
        var document = JsonSerializer.Deserialize<GroupDocument>(raw, JsonOptions)!;
        if (!structureOnly && catalog is not null)
        {
            var analysis = GalgroupCompiler.Analyze(document, catalog);
            compilation = analysis.Compilation;
            foreach (var issue in analysis.Issues)
                Error(diagnostics, issue.IsParameterError ? "entry.parametersInvalid" : "entry.compileInvalid", issue.Message, file,
                    $"/entries/{issue.SourceIndex}" + (issue.IsParameterError ? "/parameters" : ""), document.Entries[issue.SourceIndex].Id);
        }
        return diagnostics.Skip(start).Any(item => item.Severity == "error") ? null : document;
    }

    public void ValidateGraph(JsonElement? content, List<ToolDiagnostic> diagnostics, bool structureOnly = false, bool checkSourceReferences = true, bool validateConditionSyntax = true)
    {
        if (content is null || !Shape(content.Value, GraphFile, "", diagnostics, ["version", "name", "rootNodeId", "nodes", "edges", "playerVariables", "saveVariables"])) return;
        var graph = content.Value;
        var start = diagnostics.Count;
        DuplicateProperties(graph, GraphFile, "", diagnostics);
        Version(graph, 2, GraphFile, diagnostics);
        String(graph, "name", GraphFile, "", diagnostics, required: true);
        String(graph, "rootNodeId", GraphFile, "", diagnostics, required: true, allowEmpty: true);
        var nodes = ProjectRawReader.Array(graph, "nodes", GraphFile, diagnostics);
        var edges = ProjectRawReader.Array(graph, "edges", GraphFile, diagnostics);
        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        var edgeIds = new HashSet<string>(StringComparer.Ordinal);
        var options = new HashSet<string>(StringComparer.Ordinal);
        var conditions = new HashSet<string>(StringComparer.Ordinal);
        var sourceFiles = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i]; var pointer = $"/nodes/{i}";
            if (!Shape(node, GraphFile, pointer, diagnostics, ["id", "type", "name", "x", "y", "file", "branchType", "options", "conditions"])) continue;
            Identity(node, "id", nodeIds, GraphFile, pointer, diagnostics);
            String(node, "name", GraphFile, pointer, diagnostics, required: true);
            foreach (var key in new[] { "file", "branchType" })
                if (node.TryGetProperty(key, out var optional) && optional.ValueKind != JsonValueKind.Null)
                    String(node, key, GraphFile, pointer, diagnostics);
            foreach (var key in new[] { "x", "y" })
                if (node.TryGetProperty(key, out var coord) && (coord.ValueKind != JsonValueKind.Number || !coord.TryGetDouble(out var coordinate) || !double.IsFinite(coordinate)))
                    Error(diagnostics, "graph.coordinateInvalid", "Coordinate must be a number.", GraphFile, pointer + "/" + key);
            var type = ProjectRawReader.Text(node, "type");
            if (type is not ("Entry" or "Group" or "Branch")) Error(diagnostics, "graph.nodeTypeInvalid", "Node type must be Entry, Group or Branch.", GraphFile, pointer + "/type");
            if (type == "Group")
            {
                String(node, "file", GraphFile, pointer, diagnostics, required: true);
                if (!structureOnly && checkSourceReferences && ProjectRawReader.Text(node, "file") is { } file)
                    try
                    {
                        if (Path.IsPathRooted(file)) throw new InvalidDataException("Group file must be relative to the graph directory.");
                        var relative = _reader.Relative(_reader.Resolve(Path.Combine(Path.GetDirectoryName(GraphFile) ?? "", file)));
                        if (!relative.EndsWith(".rawgalgroup", StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("Group source must be a .rawgalgroup within the project.");
                        if (!sourceFiles.Add(relative)) Error(diagnostics, "group.sourceDuplicate", "Multiple Groups reference the same source.", GraphFile, pointer + "/file");
                        if (!File.Exists(_reader.Resolve(relative))) Error(diagnostics, "group.sourceMissing", "Group source file is missing.", GraphFile, pointer + "/file");
                    }
                    catch (Exception exception) when (exception is ArgumentException or IOException or InvalidDataException)
                    { Error(diagnostics, "group.pathInvalid", exception.Message, GraphFile, pointer + "/file"); }
            }
            if (type == "Branch" && ProjectRawReader.Text(node, "branchType") is not ("Choice" or "Condition"))
                Error(diagnostics, "graph.branchTypeInvalid", "Branch type must be Choice or Condition.", GraphFile, pointer + "/branchType");
            foreach (var key in new[] { "options", "conditions" })
            {
                if (!node.TryGetProperty(key, out var collection) || collection.ValueKind == JsonValueKind.Null) continue;
                if (collection.ValueKind != JsonValueKind.Array) { Error(diagnostics, "json.arrayRequired", "Expected an array.", GraphFile, pointer + "/" + key); continue; }
                var n = 0;
                foreach (var item in collection.EnumerateArray())
                {
                    var child = pointer + $"/{key}/{n++}";
                    if (!Shape(item, GraphFile, child, diagnostics, key == "options" ? ["id", "text", "condition"] : ["id", "expression"])) continue;
                    Identity(item, "id", key == "options" ? options : conditions, GraphFile, child, diagnostics);
                    String(item, key == "options" ? "text" : "expression", GraphFile, child, diagnostics, required: true, allowEmpty: key == "options" || !validateConditionSyntax);
                    if (key == "options") String(item, "condition", GraphFile, child, diagnostics);
                    if (!structureOnly && validateConditionSyntax) Condition(ProjectRawReader.Text(item, key == "options" ? "condition" : "expression"), GraphFile, child, diagnostics);
                }
            }
        }
        for (var i = 0; i < edges.Count; i++)
        {
            var edge = edges[i]; var pointer = $"/edges/{i}";
            if (!Shape(edge, GraphFile, pointer, diagnostics, ["id", "fromNodeId", "fromOutlet", "toNodeId"])) continue;
            Identity(edge, "id", edgeIds, GraphFile, pointer, diagnostics);
            String(edge, "fromNodeId", GraphFile, pointer, diagnostics, required: true);
            String(edge, "toNodeId", GraphFile, pointer, diagnostics, required: true);
            if (!edge.TryGetProperty("fromOutlet", out var outlet) || outlet.ValueKind != JsonValueKind.Number || !outlet.TryGetInt32(out _))
                Error(diagnostics, "graph.outletInvalid", "Outlet must be an integer.", GraphFile, pointer + "/fromOutlet");
        }
        var variableNames = new HashSet<string>(StringComparer.Ordinal);
        var variableUids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in new[] { "playerVariables", "saveVariables" })
        {
            var variables = ProjectRawReader.Array(graph, key, GraphFile, diagnostics);
            for (var i = 0; i < variables.Count; i++)
            {
                var variable = variables[i]; var pointer = $"/{key}/{i}";
                if (!Shape(variable, GraphFile, pointer, diagnostics, ["name", "defaultValue", "type", "description"])) continue;
                String(variable, "description", GraphFile, pointer, diagnostics);
                Identity(variable, "name", variableNames, GraphFile, pointer, diagnostics);
                if (!variable.TryGetProperty("defaultValue", out var value) || !Shape(value, GraphFile, pointer + "/defaultValue", diagnostics, ["Uid", "Name", "Type", "Value"]))
                { Error(diagnostics, "variable.defaultRequired", "Typed defaultValue is required.", GraphFile, pointer); continue; }
                Identity(value, "Uid", variableUids, GraphFile, pointer + "/defaultValue", diagnostics);
                try
                {
                    var typed = JsonSerializer.Deserialize<Variable>(value)!;
                    if (typed.Name != ProjectRawReader.Text(variable, "name")) throw new InvalidDataException("Default variable Name must match declaration name.");
                    if (variable.TryGetProperty("type", out var declaredType) && (declaredType.ValueKind != JsonValueKind.Number || declaredType.GetInt32() != (int)typed.Type))
                        throw new InvalidDataException("Declaration type must match the default value Type.");
                }
                catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or InvalidDataException or FormatException)
                { Error(diagnostics, "variable.defaultInvalid", exception.Message, GraphFile, pointer + "/defaultValue"); }
            }
        }
        if (diagnostics.Skip(start).Any(item => item.Code is "json.objectRequired" or "json.arrayRequired" or "variable.defaultRequired")) return;
        EditorGraphDocument document;
        try { document = JsonSerializer.Deserialize<EditorGraphDocument>(graph, JsonOptions)!; }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { Error(diagnostics, "graph.shapeInvalid", exception.Message, GraphFile, ""); return; }
        if (document.Nodes.Count != 0 && string.IsNullOrWhiteSpace(document.RootNodeId)) Error(diagnostics, "graph.rootRequired", "A nonempty graph requires a root node.", GraphFile, "/rootNodeId");
        var occupied = new HashSet<(string, int)>();
        foreach (var (edge, index) in document.Edges.Select((edge, index) => (edge, index)))
        {
            if (!occupied.Add((edge.FromNodeId, edge.FromOutlet))) Error(diagnostics, "graph.outletDuplicate", "An outlet can have only one outgoing edge.", GraphFile, $"/edges/{index}", edge.Id);
        }
        foreach (var diagnostic in EditorDocumentValidator.Validate(new EditorProjectDocument { Graph = document }).Diagnostics)
        {
            var objectId = diagnostic.Resource?.Split('/').Last();
            var edgeIndex = document.Edges.FindIndex(edge => edge.Id == objectId);
            var pointer = diagnostic.Code.StartsWith("graph.root", StringComparison.Ordinal) ? "/rootNodeId" : edgeIndex >= 0 ? $"/edges/{edgeIndex}" : "";
            Error(diagnostics, diagnostic.Code, diagnostic.Message, GraphFile, pointer, objectId);
        }
    }

    private static void Condition(string? expression, string file, string pointer, IList<ToolDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(expression)) return;
        var parsed = new NCalc.Expression(expression.Replace('"', '\''));
        if (parsed.HasErrors()) Error(diagnostics, "condition.syntaxInvalid", parsed.Error, file, pointer);
    }

    private static void Version(JsonElement value, int expected, string file, IList<ToolDiagnostic> diagnostics)
    {
        if (!value.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != expected)
            Error(diagnostics, "document.versionInvalid", $"Explicit current version {expected} is required.", file, "/version");
    }
    private static bool Shape(JsonElement value, string file, string pointer, IList<ToolDiagnostic> diagnostics, string[] allowed)
    {
        if (!Object(value, file, pointer, diagnostics)) return false;
        foreach (var property in value.EnumerateObject())
            if (!allowed.Contains(property.Name, StringComparer.Ordinal)) Error(diagnostics, "json.fieldUnknown", $"Unknown field '{property.Name}'.", file, pointer + "/" + Escape(property.Name));
        return true;
    }
    private static bool Object(JsonElement value, string file, string pointer, IList<ToolDiagnostic> diagnostics)
    {
        if (value.ValueKind == JsonValueKind.Object) return true;
        Error(diagnostics, "json.objectRequired", "Expected an object.", file, pointer); return false;
    }
    private static void String(JsonElement value, string key, string file, string pointer, IList<ToolDiagnostic> diagnostics, bool required = false, bool allowEmpty = false)
    {
        if (!value.TryGetProperty(key, out var property)) { if (!required) return; }
        else if (property.ValueKind == JsonValueKind.String && (allowEmpty || !required || !string.IsNullOrWhiteSpace(property.GetString()))) return;
        Error(diagnostics, "json.stringRequired", $"'{key}' must be {(required && !allowEmpty ? "a nonempty" : "a")} string.", file, pointer + "/" + Escape(key));
    }
    private static void Identity(JsonElement value, string key, HashSet<string> seen, string file, string pointer, IList<ToolDiagnostic> diagnostics)
    {
        String(value, key, file, pointer, diagnostics, required: true);
        if (ProjectRawReader.Text(value, key) is { Length: > 0 } id && !seen.Add(id)) Error(diagnostics, "identity.duplicate", $"Duplicate '{key}' value '{id}'.", file, pointer + "/" + Escape(key), id);
    }
    public static void DuplicateProperties(JsonElement value, string file, string pointer, IList<ToolDiagnostic> diagnostics)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                var child = pointer + "/" + Escape(property.Name);
                if (!seen.Add(property.Name)) Error(diagnostics, "json.propertyDuplicate", "Duplicate JSON property.", file, child);
                DuplicateProperties(property.Value, file, child, diagnostics);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        { var i = 0; foreach (var item in value.EnumerateArray()) DuplicateProperties(item, file, pointer + "/" + i++, diagnostics); }
    }
    private static string Escape(string value) => value.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
    private static void Error(IList<ToolDiagnostic> diagnostics, string code, string message, string file, string pointer, string? id = null) =>
        diagnostics.Add(new(code, "error", message, file, id, pointer));
}
