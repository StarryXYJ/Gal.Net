using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GalNet.Core.Compilation;
using GalNet.Core.Entry;
using GalNet.Core.Serialization;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;

namespace GalNet.Editor.Shared.Services;

public sealed class EditorSaveCoordinator : IEditorSaveCoordinator
{
    private readonly IEditorDocumentRepository _repository;
    private readonly IEntryCatalog _catalog;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public EditorSaveCoordinator(IEditorDocumentRepository repository, IEntryCatalog catalog)
    {
        _repository = repository;
        _catalog = catalog;
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
        for (var directory = new DirectoryInfo(Path.GetFullPath(previewPath)); directory is not null; directory = directory.Parent)
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Preview output must not traverse directory links.");
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
                File = node.Type == "Group" ? PreviewFile(node.Id) : null,
                BranchType = node.BranchType,
                Options = node.Options?.Select(option => new EditorGraphBranchOptionDto
                {
                    Id = option.Id,
                    Text = option.Text,
                    Condition = option.Condition
                }).ToList(),
                Conditions = node.Conditions?.Select(condition => new EditorGraphBranchConditionDto
                {
                    Id = condition.Id,
                    Expression = condition.Expression
                }).ToList()
            }).ToList(),
            Edges = document.Edges.Select(edge => new EditorGraphEdgeDto
            {
                Id = edge.Id,
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
            var raw = new GroupDocument { Kind = GroupDocumentKind.Raw, Entries = entries.Select(SerializeEntry).ToList() };
            var compiled = GalgroupCompiler.Compile(raw, _catalog).Document;
            File.WriteAllText(Path.Combine(previewPath, PreviewFile(groupId)), JsonSerializer.Serialize(compiled, JsonOptions));
        }

        return previewPath;
    }

    private static string PreviewFile(string id) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id))) + ".galgroup";
    private GroupEntryDocument SerializeEntry(EditorEntryData entry) => new EditorDocumentRepository(_catalog).SerializeEntry(entry);
}
