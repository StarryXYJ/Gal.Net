using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GalNet.Core.Settings;
using GalNet.Core.Variable;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;
using GalNet.Core.Entry;

namespace GalNet.Editor.Shared.Services;

public sealed class EditorDocumentRepository : IEditorDocumentRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private const string StableIdParameter = "__editorId";

    public LoadedEditorProjectDocument Load(string projectPath, string projectName, ProjectSettings settings)
    {
        var graphPath = Path.Combine(projectPath, "Graph");
        var graphFile = Path.Combine(graphPath, "graph.json");
        EditorGraphDocument document;

        if (File.Exists(graphFile))
        {
            var json = File.ReadAllText(graphFile);
            document = JsonSerializer.Deserialize<EditorGraphDocument>(json) ?? new EditorGraphDocument();
        }
        else
        {
            document = CreateDefaultDocument(projectName);
        }

        document.Name = string.IsNullOrWhiteSpace(document.Name) ? projectName : document.Name;
        MigrateVariableDefinitions(document, settings);
        EnsureStableIds(document);

        var loaded = new LoadedEditorProjectDocument { Document = document };
        foreach (var groupNode in document.Nodes.Where(n => string.Equals(n.Type, "Group", StringComparison.OrdinalIgnoreCase)))
        {
            loaded.GroupEntries[groupNode.Id] = LoadGroupEntries(graphPath, groupNode.File);
        }

        return loaded;
    }

    public void Save(
        string projectPath,
        EditorGraphDocument document,
        IReadOnlyDictionary<string, IReadOnlyList<EditorEntryData>> groupEntries)
    {
        var graphPath = Path.Combine(projectPath, "Graph");
        var groupsDir = Path.Combine(graphPath, "groups");
        Directory.CreateDirectory(groupsDir);

        foreach (var groupNode in document.Nodes.Where(n => string.Equals(n.Type, "Group", StringComparison.OrdinalIgnoreCase)))
        {
            var relativeFile = string.IsNullOrWhiteSpace(groupNode.File)
                ? $"groups/{groupNode.Id}.galgroup"
                : groupNode.File!;
            groupNode.File = relativeFile;

            groupEntries.TryGetValue(groupNode.Id, out var entries);
            var serialized = (entries ?? []).Select(SerializeEntry).ToArray();
            var groupFile = Path.Combine(graphPath, relativeFile.Replace('/', Path.DirectorySeparatorChar));
            var groupTemporary = groupFile + ".tmp";
            File.WriteAllLines(groupTemporary, serialized);
            File.Move(groupTemporary, groupFile, true);
        }

        var graphFile = Path.Combine(graphPath, "graph.json");
        var graphTemporary = graphFile + ".tmp";
        File.WriteAllText(graphTemporary, JsonSerializer.Serialize(document, JsonOptions));
        File.Move(graphTemporary, graphFile, true);
    }

    private static EditorGraphDocument CreateDefaultDocument(string projectName)
    {
        var entryId = Guid.NewGuid().ToString("N");
        var groupId = Guid.NewGuid().ToString("N");
        return new EditorGraphDocument
        {
            Name = projectName,
            RootNodeId = entryId,
            Nodes =
            [
                new EditorGraphNodeDto
                {
                    Id = entryId,
                    Type = "Entry",
                    Name = "Entry",
                    X = 4620,
                    Y = 4950
                },
                new EditorGraphNodeDto
                {
                    Id = groupId,
                    Type = "Group",
                    Name = "Opening",
                    X = 4900,
                    Y = 4950,
                    File = $"groups/{groupId}.galgroup"
                }
            ],
            Edges =
            [
                new EditorGraphEdgeDto
                {
                    FromNodeId = entryId,
                    FromOutlet = 0,
                    ToNodeId = groupId
                }
            ]
        };
    }

    private static void MigrateVariableDefinitions(EditorGraphDocument document, ProjectSettings settings)
    {
        if (document.PlayerVariables.Count == 0 && settings.PlayerVariables.Count > 0)
            document.PlayerVariables = settings.PlayerVariables.Select(v => v.Clone()).ToList();

        if (document.SaveVariables.Count == 0 && settings.SaveVariables.Count > 0)
            document.SaveVariables = settings.SaveVariables.Select(v => v.Clone()).ToList();

        VariableNameRules.Normalize(document.PlayerVariables, document.SaveVariables);
    }

    private static List<EditorEntryData> LoadGroupEntries(string graphPath, string? relativeFile)
    {
        var entries = new List<EditorEntryData>();
        if (string.IsNullOrWhiteSpace(relativeFile))
            return entries;

        var file = Path.Combine(graphPath, relativeFile.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(file))
            return entries;

        var parsed = GalNet.Core.Serialization.GalgroupParser.Parse(File.ReadAllText(file));
        foreach (var entry in parsed)
        {
            var definition = EntryRegistry.Get(entry.EntryType);
            var parameters = entry.Params
                .Where(p => p.Key is not "condition" and not StableIdParameter)
                .Where(p => definition.Parameters.ContainsKey(p.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

            entries.Add(new EditorEntryData
            {
                StableId = entry.Params.TryGetValue(StableIdParameter, out var stableId) && !string.IsNullOrWhiteSpace(stableId)
                    ? stableId
                    : Guid.NewGuid().ToString("N"),
                Id = entries.Count + 1,
                Type = entry.EntryType,
                Condition = entry.Params.TryGetValue("condition", out var condition) ? condition : "",
                Parameters = parameters
            });
        }

        return entries;
    }

    private static string SerializeEntry(EditorEntryData entry)
    {
        var definition = EntryRegistry.Get(entry.Type);
        var parameters = entry.Parameters
            .Where(pair => definition.Parameters.ContainsKey(pair.Key) && pair.Value.Length > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(entry.Condition))
            parameters["condition"] = entry.Condition;

        entry.StableId = string.IsNullOrWhiteSpace(entry.StableId)
            ? Guid.NewGuid().ToString("N")
            : entry.StableId;
        parameters[StableIdParameter] = entry.StableId;

        return GalNet.Core.Serialization.GalgroupParser.Serialize(entry.Type, parameters);
    }

    private static void EnsureStableIds(EditorGraphDocument document)
    {
        foreach (var edge in document.Edges)
            edge.Id = EnsureId(edge.Id);

        foreach (var node in document.Nodes)
        {
            foreach (var option in node.Options ?? [])
                option.Id = EnsureId(option.Id);
            foreach (var condition in node.Conditions ?? [])
                condition.Id = EnsureId(condition.Id);
        }
    }

    private static string EnsureId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value;
}
