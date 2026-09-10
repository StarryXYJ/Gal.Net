using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;
using GalNet.Core.Entry;
using GalNet.Core.Serialization;

namespace GalNet.Editor.Shared.Services;

public sealed class EditorSaveCoordinator : IEditorSaveCoordinator
{
    private readonly IEditorDocumentRepository _repository;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public EditorSaveCoordinator(IEditorDocumentRepository repository)
    {
        _repository = repository;
    }

    public void SaveProjectDocument(
        string projectPath,
        EditorGraphDocument document,
        IReadOnlyDictionary<string, IReadOnlyList<EditorEntryData>> groupEntries)
    {
        _repository.Save(projectPath, document, groupEntries);
    }

    public string BuildPreviewData(
        string previewPath,
        EditorGraphDocument document,
        IReadOnlyDictionary<string, IReadOnlyList<EditorEntryData>> groupEntries)
    {
        Directory.CreateDirectory(previewPath);

        foreach (var file in Directory.EnumerateFiles(previewPath, "*.galgroup"))
            File.Delete(file);

        var previewDocument = new EditorGraphDocument
        {
            Version = document.Version,
            Name = document.Name,
            RootNodeId = document.RootNodeId,
            Nodes = document.Nodes.Select(node => new EditorGraphNodeDto
            {
                Id = node.Id,
                Type = node.Type,
                Name = node.Name,
                X = node.X,
                Y = node.Y,
                File = null,
                BranchType = node.BranchType,
                Options = node.Options?.Select(option => new EditorGraphBranchOptionDto
                {
                    Text = option.Text,
                    Condition = option.Condition
                }).ToList(),
                Conditions = node.Conditions?.Select(condition => new EditorGraphBranchConditionDto
                {
                    Expression = condition.Expression
                }).ToList()
            }).ToList(),
            Edges = document.Edges.Select(edge => new EditorGraphEdgeDto
            {
                FromNodeId = edge.FromNodeId,
                FromOutlet = edge.FromOutlet,
                ToNodeId = edge.ToNodeId
            }).ToList(),
            PlayerVariables = document.PlayerVariables.Select(v => v.Clone()).ToList(),
            SaveVariables = document.SaveVariables.Select(v => v.Clone()).ToList()
        };

        File.WriteAllText(Path.Combine(previewPath, "graph.json"), JsonSerializer.Serialize(previewDocument, JsonOptions));
        foreach (var (groupId, entries) in groupEntries)
        {
            var serialized = new GroupDocument { Entries = entries.Select(SerializeEntry).ToList() };
            File.WriteAllText(Path.Combine(previewPath, $"{groupId}.galgroup"), JsonSerializer.Serialize(serialized, JsonOptions));
        }

        return previewPath;
    }

    private static GroupEntryDocument SerializeEntry(EditorEntryData entry)
    {
        var definition = EntryRegistry.Get(entry.Type);
        var parameters = entry.Parameters
            .Where(pair => definition.Parameters.ContainsKey(pair.Key) && pair.Value.Length > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        entry.StableId = string.IsNullOrWhiteSpace(entry.StableId) ? Guid.NewGuid().ToString("N") : entry.StableId;
        return new GroupEntryDocument
        {
            Id = entry.StableId,
            Type = entry.Type,
            Condition = entry.Condition,
            Parameters = parameters.ToDictionary(pair => pair.Key, pair => ToJsonValue(pair.Key, pair.Value), StringComparer.Ordinal)
        };
    }

    private static JsonElement ToJsonValue(string name, string value)
    {
        if (name == "transform")
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(value) ? "{}" : value);
            return document.RootElement.Clone();
        }
        return JsonSerializer.SerializeToElement(value);
    }
}
