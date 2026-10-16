using System.Text.Json;
using System.Text.Json.Nodes;
using GalNet.Core.Entry;
using GalNet.Core.Serialization;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Shared.Commands;

/// <summary>Shared disk-operation boundary. CLI parsing and editor presentation stay outside it.</summary>
public sealed class ProjectEditService(string projectPath, IEntryCatalog catalog)
{
    public Task<ToolResult> ExecuteGroupAsync(IProjectEditCommand command, bool dryRun = false, CancellationToken cancellationToken = default)
    {
        var (id, operation) = command switch
        {
            CreateNodeCommand { Kind: EditorNodeKind.LinearGroup } create => (create.NodeId, "group.create"),
            RenameNodeCommand rename => (rename.NodeId, "group.rename"),
            DeleteNodeCommand delete => (delete.NodeId, "group.delete"),
            _ => throw new ArgumentException("A Group lifecycle operation is required.")
        };
        if (command is not CreateNodeCommand)
        {
            var result = new ProjectQueryService(projectPath).Graph("graph.node", id);
            var page = (QueryPage<ProjectObjectItem>)result.Data!;
            if (!result.Success || page.Items.Count != 1 || ProjectRawReader.Text(page.Items[0].Content, "type") != "Group")
                return Task.FromResult(new ToolResult(operation, false, null,
                    [new("group.notFound", "error", "The ID must identify one existing Group node.", ObjectId: id)]));
        }
        return ExecuteAsync([command], dryRun, operation, cancellationToken);
    }

    public async Task<ToolResult> ExecuteAsync(IReadOnlyList<IProjectEditCommand> commands, bool dryRun = false,
        string operationId = "execute", CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<ToolDiagnostic>();
        try
        {
            if (commands.Count == 0) throw new ArgumentException("At least one command is required.");
            var persistence = new DirectProjectPersistence(projectPath, new EditorDocumentRepository(catalog));
            var document = persistence.Load();
            var reader = new ProjectRawReader(projectPath);
            var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var ownedFiles = document.Graph.Nodes.Where(node => node.Type == "Group")
                .Select(node => reader.Relative(reader.Resolve("Graph/" + node.File))).Append("Graph/graph.json").ToHashSet(pathComparer);
            var handler = new BuiltInEditorCommandHandler(catalog);
            var descriptions = new List<string>();
            var changed = new List<string>();
            var references = new List<ProjectReferenceItem>();
            bool? referencesComplete = null;
            foreach (var command in commands)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (command is DeleteNodeCommand deletion && document.Graph.Nodes.FirstOrDefault(node => node.Id == deletion.NodeId) is { Type: not "Entry" } node)
                {
                    var scan = new ProjectQueryService(projectPath).References(node.Id, node.Type == "Group" ? "group" : "node");
                    var page = (QueryPage<ProjectReferenceItem>)scan.Data!;
                    referencesComplete = false;
                    var candidates = page.Items.Where(item => !ownedFiles.Contains(item.File)).ToList();
                    var kind = node.Type == "Group" ? "group" : "node";
                    ProjectQueryService.Scan(JsonSerializer.SerializeToElement(document.Graph), "", "Graph/graph.json", node.Id, kind, candidates);
                    var serializer = new EditorDocumentRepository(catalog);
                    foreach (var group in document.Graph.Nodes.Where(item => item.Type == "Group"))
                        ProjectQueryService.Scan(JsonSerializer.SerializeToElement(new GroupDocument
                        { Kind = GroupDocumentKind.Raw, Entries = document.GroupEntries[group.Id].Select(serializer.SerializeEntry).ToList() }),
                            "", reader.Relative(reader.Resolve("Graph/" + group.File)), node.Id, kind, candidates);
                    var ownFile = node.Type == "Group" && deletion.DeleteGroupEntries && node.File is not null
                        ? new ProjectRawReader(projectPath).Relative(new ProjectRawReader(projectPath).Resolve("Graph/" + node.File)) : null;
                    var external = candidates.Where(item => !pathComparer.Equals(item.File, ownFile) &&
                        !(pathComparer.Equals(item.File, "Graph/graph.json") && (item.JsonPointer == "/rootNodeId" || item.JsonPointer.StartsWith("/edges/", StringComparison.Ordinal)))).ToArray();
                    references.AddRange(external);
                    diagnostics.AddRange(scan.Diagnostics.Select(item => deletion.ForceReferences && item.Severity == "error" ? item with { Severity = "warning" } : item));
                    if (!dryRun && !deletion.ForceReferences && (!scan.Success || external.Length != 0))
                    {
                        diagnostics.Add(new("references.forceRequired", "error", "Deletion has external reference candidates or an incomplete scan. Review references and explicitly acknowledge them with forceReferences/--force.", ObjectId: node.Id));
                        return new(operationId, false, new { references = external, referencesComplete = false }, diagnostics);
                    }
                }
                var execution = handler.Execute(document, command, new(0, false));
                diagnostics.AddRange(execution.Diagnostics.Select(item => new ToolDiagnostic(item.Code, item.Severity.ToString().ToLowerInvariant(), item.Message, item.Resource)));
                if (!execution.Success) return new(operationId, false, null, diagnostics);
                changed.AddRange(execution.ChangedResources);
                if (execution.Description is not null) descriptions.Add(execution.Description.Description);
            }
            var plan = persistence.PrepareSave(document);
            var report = new ProjectEditReport(dryRun, descriptions, changed.Distinct().ToArray(), plan.Writes.Keys.Order(StringComparer.Ordinal).ToArray(),
                plan.DeletedFiles, commands.OfType<CreateNodeCommand>().Select(command => command.NodeId).ToArray(), references, referencesComplete);
            if (!dryRun) await persistence.SaveAsync(document, cancellationToken);
            return new(operationId, !diagnostics.Any(item => item.Severity == "error"), report, diagnostics);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException)
        {
            diagnostics.Add(new("project.editRejected", "error", exception.Message));
            return new(operationId, false, null, diagnostics);
        }
    }

    public async Task<ToolResult> ExecuteJsonAsync(string json, CancellationToken cancellationToken = default)
    {
        const string operationId = "execute";
        try
        {
            using var parsed = JsonDocument.Parse(json);
            var root = parsed.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new JsonException("An operation envelope must be an object.");
            var duplicates = new List<ToolDiagnostic>();
            ProjectValidationService.DuplicateProperties(root, "command-input", "", duplicates);
            if (duplicates.Count != 0) return new(operationId, false, null, duplicates);
            var batch = root.TryGetProperty("commands", out var array);
            if (batch && array.ValueKind != JsonValueKind.Array) throw new JsonException("commands must be an array.");
            if (batch && root.EnumerateObject().Any(property => property.Name is not ("commands" or "dryRun")))
                throw new JsonException("A batch envelope only accepts commands and dryRun.");
            var dryRun = root.TryGetProperty("dryRun", out var value) && value.GetBoolean();
            var elements = batch ? array.EnumerateArray().ToArray() : [root];
            if (elements.Length == 0) throw new JsonException("At least one command is required.");
            var documents = new EditorCommandCatalog();
            var files = new AssetFileCommandCatalog();
            var fileFlags = elements.Select(element => files.GetAll().Any(definition => definition.Id == CommandId(element))).ToArray();
            if (fileFlags.Any(flag => flag))
            {
                if (elements.Length != 1 || !fileFlags[0]) throw new JsonException("File commands cannot be mixed with document commands.");
                if (dryRun) throw new JsonException("File operations do not support dryRun; no files were changed.");
                var payload = Payload(elements[0], !batch);
                var command = files.Deserialize(CommandId(payload), payload, CommandJson.Options);
                var result = await new AssetFileCommandExecutor(catalog).ExecuteAsync(projectPath, command, cancellationToken);
                return new(operationId, result.Success, result,
                    result.Diagnostics.Select(item => new ToolDiagnostic(item.Code, item.Severity.ToString().ToLowerInvariant(), item.Message, item.Resource)).ToArray());
            }
            var commands = elements.Select(element =>
            {
                var payload = Payload(element, !batch);
                return documents.Deserialize(CommandId(payload), payload, CommandJson.Options);
            }).ToArray();
            return await ExecuteAsync(commands, dryRun, cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or KeyNotFoundException or InvalidOperationException)
        { return new(operationId, false, null, [new("command.arguments", "error", exception.Message)]); }
    }

    private static JsonElement Payload(JsonElement element, bool removeEnvelope)
    {
        if (!removeEnvelope) return element;
        var node = JsonNode.Parse(element.GetRawText())!.AsObject();
        node.Remove("dryRun");
        return JsonSerializer.SerializeToElement(node);
    }

    private static string CommandId(JsonElement element) => element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty("commandId", out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
        ? value.GetString()! : throw new JsonException("Every command requires a nonempty commandId.");
}
