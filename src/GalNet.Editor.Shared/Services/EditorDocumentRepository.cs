using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GalNet.Core.Settings;
using GalNet.Core.Variable;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;

namespace GalNet.Editor.Shared.Services;

public sealed class EditorDocumentRepository : IEditorDocumentRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

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
            File.WriteAllLines(Path.Combine(graphPath, relativeFile.Replace('/', Path.DirectorySeparatorChar)), serialized);
        }

        File.WriteAllText(Path.Combine(graphPath, "graph.json"), JsonSerializer.Serialize(document, JsonOptions));
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
            var parameters = entry.Params
                .Where(p => p.Key != "condition")
                .Select(p => string.IsNullOrEmpty(p.Value) ? p.Key : $"{p.Key}={p.Value}");

            entries.Add(new EditorEntryData
            {
                Id = entries.Count + 1,
                Type = entry.EntryType,
                Condition = entry.Params.TryGetValue("condition", out var condition) ? condition : "",
                Parameters = string.Join("; ", parameters)
            });
        }

        return entries;
    }

    private static string SerializeEntry(EditorEntryData entry)
    {
        var parameters = entry.Parameters
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]))
            .ToDictionary(parts => parts[0], parts => parts.Length > 1 ? parts[1] : "");

        if (!string.IsNullOrWhiteSpace(entry.Condition))
            parameters["condition"] = entry.Condition;

        return GalNet.Core.Serialization.GalgroupParser.Serialize(entry.Type, parameters);
    }
}
