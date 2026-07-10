using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;

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
            var serialized = entries.Select(SerializeEntry).ToArray();
            File.WriteAllLines(Path.Combine(previewPath, $"{groupId}.galgroup"), serialized);
        }

        return previewPath;
    }

    private static string SerializeEntry(EditorEntryData entry)
    {
        var parameters = entry.Parameters
            .Split(';', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', 2, System.StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]))
            .ToDictionary(parts => parts[0], parts => parts.Length > 1 ? parts[1] : "");

        if (!string.IsNullOrWhiteSpace(entry.Condition))
            parameters["condition"] = entry.Condition;

        return GalNet.Core.Serialization.GalgroupParser.Serialize(entry.Type, parameters);
    }
}
