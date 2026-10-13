using System.Text.Json;
using System.Text.RegularExpressions;
using GalNet.Editor.Abstraction.Commands;

namespace GalNet.Editor.Shared.Services;

/// <summary>Reusable project queries over unchanged author files; no catalog or engine required.</summary>
public sealed class ProjectQueryService(string projectDirectory)
{
    private static readonly string[] VariableScopes = ["player", "save"];
    private readonly ProjectRawReader _reader = new(projectDirectory);
    private const string GraphFile = "Graph/graph.json";

    public ToolResult Files(ProjectFileQuery query)
    {
        var diagnostics = new List<ToolDiagnostic>();
        var pattern = query.Glob is null ? null : BuildGlob(query.Glob);
        var items = new List<ProjectFileItem>();
        foreach (var file in _reader.Files(query.Root, diagnostics))
        {
            var kind = Kind(file);
            if (query.Kind is not null && kind != query.Kind ||
                query.Extension is not null && !Path.GetExtension(file).Equals("." + query.Extension.TrimStart('.'), StringComparison.OrdinalIgnoreCase) ||
                pattern is not null && !pattern.IsMatch(file)) continue;
            string? resourceId = null;
            var metaFile = kind == "metadata" ? file : kind == "resource" && File.Exists(Path.Combine(_reader.Root, file + ".meta")) ? file + ".meta" : null;
            if (metaFile is not null && _reader.Read(metaFile, diagnostics) is { } meta)
            {
                resourceId = ProjectRawReader.Text(meta, "Id");
                if (!Guid.TryParse(resourceId, out _))
                    diagnostics.Add(new("asset.guidInvalid", "error", "Metadata Id must be a resource GUID.", metaFile, JsonPointer: "/Id"));
            }
            items.Add(new(file, kind, new FileInfo(_reader.Resolve(file)).Length, resourceId));
        }
        return Page("files", query.Root ?? "author-files", items, diagnostics, query.Offset, query.Limit);
    }

    public ToolResult Graph(string operation, string? id = null, string? type = null, int offset = 0, int? limit = null)
    {
        var diagnostics = new List<ToolDiagnostic>();
        var graph = _reader.Read(GraphFile, diagnostics);
        if (operation == "graph.show") return Result(operation, new RawDocumentQuery(GraphFile, graph), diagnostics);
        var property = operation == "graph.edges" ? "edges" : "nodes";
        var items = ProjectRawReader.Array(graph, property, GraphFile, diagnostics)
            .Select((node, index) => new ProjectObjectItem(property == "edges" ? "edge" : "node", ProjectRawReader.Text(node, "id"), GraphFile, $"/{property}/{index}", node))
            .Where(item => (id is null || item.Id == id) && (type is null || ProjectRawReader.Text(item.Content, "type") == type)).ToArray();
        if (id is not null && items.Length == 0 && diagnostics.Count == 0)
            diagnostics.Add(new("object.notFound", "error", $"Node '{id}' was not found.", GraphFile, id));
        return Page(operation, GraphFile, items, diagnostics, offset, limit);
    }

    public ToolResult Groups(int offset = 0, int? limit = null)
    {
        var diagnostics = new List<ToolDiagnostic>();
        return Page("group.list", "groups", ReadGroups(diagnostics), diagnostics, offset, limit);
    }

    public ToolResult Group(GroupSelector selector, bool entries, string? entryType = null, string? entryId = null, int offset = 0, int? limit = null)
    {
        var diagnostics = new List<ToolDiagnostic>();
        var file = ResolveGroup(selector, diagnostics);
        var content = file is null ? null : _reader.Read(file, diagnostics);
        if (!entries) return Result("group.show", new RawDocumentQuery(file ?? "group", content), diagnostics);
        var items = ProjectRawReader.Array(content, "entries", file ?? "", diagnostics)
            .Select((entry, index) => new ProjectObjectItem("entry", ProjectRawReader.Text(entry, "id"), file!, $"/entries/{index}", entry, selector.Id))
            .Where(item => (entryType is null || ProjectRawReader.Text(item.Content, "type") == entryType) && (entryId is null || item.Id == entryId)).ToArray();
        return Page("group.entries", file ?? "group", items, diagnostics, offset, limit);
    }

    public ToolResult Find(ProjectIdentityQuery query)
    {
        if (string.IsNullOrWhiteSpace(query.Id)) throw new ArgumentException("An ID is required.");
        if (query.Kind is not null && query.Kind is not ("resource" or "node" or "entry")) throw new ArgumentException("Unknown identity kind.");
        if (query.GroupId is not null && query.Kind is not null and not "entry") throw new ArgumentException("--group only applies to entry lookup.");
        var diagnostics = new List<ToolDiagnostic>();
        var matches = new List<ProjectObjectItem>();
        if (query.Kind is null or "node")
        {
            var graph = _reader.Read(GraphFile, diagnostics);
            matches.AddRange(ProjectRawReader.Array(graph, "nodes", GraphFile, diagnostics)
                .Select((node, index) => new ProjectObjectItem("node", ProjectRawReader.Text(node, "id"), GraphFile, $"/nodes/{index}", node))
                .Where(item => item.Id == query.Id));
        }
        if (query.Kind is null or "entry")
            foreach (var group in ReadGroups(diagnostics).Where(group => query.GroupId is null || group.Id == query.GroupId)
                         .GroupBy(group => group.File, StringComparer.Ordinal).Select(group => group.First()).Where(group => group.Exists))
            {
                var content = _reader.Read(group.File, diagnostics);
                matches.AddRange(ProjectRawReader.Array(content, "entries", group.File, diagnostics)
                    .Select((entry, index) => new ProjectObjectItem("entry", ProjectRawReader.Text(entry, "id"), group.File, $"/entries/{index}", entry, group.Id))
                    .Where(item => item.Id == query.Id));
            }
        if (query.Kind is null or "resource")
            foreach (var file in _reader.Files(null, diagnostics).Where(file => file.StartsWith("Assets/", StringComparison.Ordinal) && file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)))
                if (_reader.Read(file, diagnostics) is { } meta && Guid.TryParse(query.Id, out var requested) &&
                    Guid.TryParse(ProjectRawReader.Text(meta, "Id"), out var actual) && requested == actual)
                    matches.Add(new("resource", actual.ToString("D"), file, "/Id", meta));
        return Page("find", "project", matches.OrderBy(item => item.File, StringComparer.Ordinal).ThenBy(item => item.JsonPointer, StringComparer.Ordinal).ToArray(), diagnostics, query.Offset, query.Limit);
    }

    public ToolResult Variables(string? scope, string? name, int offset = 0, int? limit = null)
    {
        if (scope is not null && scope is not ("player" or "save")) throw new ArgumentException("Variable scope must be player or save.");
        var diagnostics = new List<ToolDiagnostic>();
        var graph = _reader.Read(GraphFile, diagnostics);
        var items = new List<ProjectObjectItem>();
        foreach (var current in VariableScopes.Where(current => scope is null || scope == current))
        {
            var key = current + "Variables";
            items.AddRange(ProjectRawReader.Array(graph, key, GraphFile, diagnostics)
                .Select((variable, index) => new ProjectObjectItem("variable", ProjectRawReader.Text(variable, "name"), GraphFile, $"/{key}/{index}", variable, current))
                .Where(item => name is null || item.Id == name));
        }
        return Page("variables", "graph-variable-definitions", items, diagnostics, offset, limit);
    }

    public ToolResult Info()
    {
        var diagnostics = new List<ToolDiagnostic>();
        var graph = _reader.Read(GraphFile, diagnostics);
        var settings = _reader.Read("settings.json", diagnostics);
        var groups = ReadGroups(diagnostics);
        return Result("project.info", new { scope = "project-info", path = _reader.Root, graph, settings, groupCount = groups.Length, validationPerformed = false }, diagnostics);
    }

    public ToolResult References(string target, string kind, int offset = 0, int? limit = null)
    {
        if (string.IsNullOrWhiteSpace(target)) throw new ArgumentException("Reference target is required.");
        if (kind is not ("resource" or "node" or "group" or "entry" or "variable")) throw new ArgumentException("Unknown reference kind.");
        var diagnostics = new List<ToolDiagnostic>();
        var items = new List<ProjectReferenceItem>();
        foreach (var file in _reader.Files(null, diagnostics).Where(file => file.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".rawgalgroup", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)))
            if (_reader.Read(file, diagnostics) is { } content)
                Scan(content, "", file, target, kind, items);
        diagnostics.Add(new("references.partial", "warning", "References are static JSON candidates. Custom C# validators/composites and runtime expressions may contain additional references; zero matches does not prove safe deletion."));
        return Page("references", "static-json-candidates", items, diagnostics, offset, limit, complete: false);
    }

    public string? ResolveGroup(GroupSelector selector, IList<ToolDiagnostic> diagnostics)
    {
        if ((selector.Id is null) == (selector.File is null)) throw new ArgumentException("Specify exactly one of --id and --file.");
        if (selector.File is not null)
        {
            var file = _reader.Relative(_reader.Resolve(selector.File));
            if (!file.EndsWith(".rawgalgroup", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Expected a .rawgalgroup source file.");
            return file;
        }
        var matches = ReadGroups(diagnostics).Where(group => group.Id == selector.Id && group.Referenced).ToArray();
        if (matches.Length == 1) return matches[0].File;
        diagnostics.Add(new(matches.Length == 0 ? "object.notFound" : "group.ambiguous", "error",
            $"Group '{selector.Id}' matched {matches.Length} graph nodes; a unique match is required.", GraphFile, selector.Id));
        return null;
    }

    private ProjectGroupItem[] ReadGroups(IList<ToolDiagnostic> diagnostics)
    {
        var graph = _reader.Read(GraphFile, diagnostics);
        var groups = new List<ProjectGroupItem>();
        foreach (var node in ProjectRawReader.Array(graph, "nodes", GraphFile, diagnostics).Where(node => ProjectRawReader.Text(node, "type") == "Group"))
        {
            var file = ProjectRawReader.Text(node, "file");
            if (string.IsNullOrWhiteSpace(file))
            { diagnostics.Add(new("group.fileRequired", "error", "Group source file is required.", GraphFile, ProjectRawReader.Text(node, "id"))); continue; }
            try
            {
                if (Path.IsPathRooted(file)) throw new ArgumentException("Group file must be relative to Graph/.");
                var full = _reader.Resolve("Graph/" + file);
                groups.Add(new(ProjectRawReader.Text(node, "id"), ProjectRawReader.Text(node, "name"), _reader.Relative(full), true, File.Exists(full)));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
            { diagnostics.Add(new("group.pathInvalid", "error", exception.Message, GraphFile, ProjectRawReader.Text(node, "id"))); }
        }
        foreach (var file in _reader.Files(null, diagnostics).Where(file => file.EndsWith(".rawgalgroup", StringComparison.OrdinalIgnoreCase)))
            if (!groups.Any(group => group.File == file)) groups.Add(new(null, null, file, false, true));
        return groups.OrderBy(group => group.File, StringComparer.Ordinal).ThenBy(group => group.Id, StringComparer.Ordinal).ToArray();
    }

    private static void Scan(JsonElement value, string pointer, string file, string target, string kind, IList<ProjectReferenceItem> items)
    {
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var property in value.EnumerateObject())
            {
                var identity = pointer.Length == 0 && file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) && property.Name == "Id" ||
                    Regex.IsMatch(pointer, @"^/(nodes|edges|entries|playerVariables|saveVariables)/[0-9]+$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) &&
                    property.Name is "id" or "uid" or "name";
                if (identity) continue;
                Scan(property.Value, pointer + "/" + property.Name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal), file, target, kind, items);
            }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var element in value.EnumerateArray()) Scan(element, $"{pointer}/{index++}", file, target, kind, items);
        }
        else if (value.ValueKind == JsonValueKind.String && value.GetString() is { } text)
        {
            var exact = text == target || kind == "resource" && Guid.TryParse(text, out var actual) && Guid.TryParse(target, out var expected) && actual == expected;
            if (exact || kind == "variable" && Regex.IsMatch(text, @"(?<![\p{L}\p{N}_])" + Regex.Escape(target) + @"(?![\p{L}\p{N}_])", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                items.Add(new(file, pointer, text, exact ? "exact-json-value-candidate" : "expression-token-candidate"));
        }
    }

    private static Regex BuildGlob(string glob)
    {
        var escaped = Regex.Escape(glob.Replace('\\', '/')).Replace(@"\*\*/", "(?:.*/)?", StringComparison.Ordinal)
            .Replace(@"\*\*", ".*", StringComparison.Ordinal).Replace(@"\*", "[^/]*", StringComparison.Ordinal).Replace(@"\?", "[^/]", StringComparison.Ordinal);
        return new Regex("^" + escaped + "$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1));
    }

    private static string Kind(string file) => file switch
    {
        GraphFile => "graph", "settings.json" => "settings",
        _ when file.EndsWith(".rawgalgroup", StringComparison.OrdinalIgnoreCase) => "rawgroup",
        _ when file.EndsWith(".galgroup", StringComparison.OrdinalIgnoreCase) => "compiledgroup",
        _ when file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) => "metadata",
        _ when file.StartsWith("Assets/", StringComparison.Ordinal) => "resource",
        _ when file.StartsWith("I18n/", StringComparison.Ordinal) => "localization",
        _ => "other"
    };

    public static ToolResult Page<T>(string operation, string scope, IReadOnlyList<T> items, IReadOnlyList<ToolDiagnostic> diagnostics,
        int offset, int? limit, bool complete = true)
    {
        if (offset < 0 || limit < 0) throw new ArgumentException("offset and limit must be nonnegative.");
        var page = items.Skip(offset).Take(limit ?? int.MaxValue).ToArray();
        return Result(operation, new QueryPage<T>(scope, items.Count, offset, page.Length, (long)offset + page.Length < items.Count,
            page, complete && !diagnostics.Any(item => item.Severity == "error" || item.Code is "file.linkSkipped" or "schema.descriptionLimited")), diagnostics);
    }

    private static ToolResult Result(string operation, object data, IReadOnlyList<ToolDiagnostic> diagnostics) =>
        new(operation, !diagnostics.Any(item => item.Severity == "error"), data, diagnostics);
}
