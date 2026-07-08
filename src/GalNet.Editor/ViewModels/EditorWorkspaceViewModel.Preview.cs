using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GalNet.Core.Variable;
using GalNet.Editor.Project;
using GalNet.Editor.Services;

namespace GalNet.Editor.ViewModels;

public partial class EditorWorkspaceViewModel
{
    public IReadOnlyList<ProjectVariableDefinition> AllProjectVariableDefinitions =>
        _projectService.Current?.Settings is { } settings
            ? settings.PlayerVariables.Concat(settings.SaveVariables).ToList()
            : [];

    public IReadOnlyList<ConditionVariableSuggestion> GetConditionVariableSuggestions()
    {
        if (_projectService.Current?.Settings is not { } settings)
            return [];

        return settings.PlayerVariables
                .Select(v => new ConditionVariableSuggestion { Name = v.Name, Scope = VariableScope.Player })
                .Concat(settings.SaveVariables.Select(v => new ConditionVariableSuggestion { Name = v.Name, Scope = VariableScope.Save }))
                .ToList();
    }

    public string BuildPreviewData()
    {
        if (_projectService.Current is not { } project)
            throw new InvalidOperationException("No project is currently open.");

        var previewPath = Path.Combine(project.TempPath, "preview");
        Directory.CreateDirectory(previewPath);

        foreach (var file in Directory.EnumerateFiles(previewPath, "*.galgroup"))
            File.Delete(file);

        var document = new EditorGraphDocument
        {
            Name = project.Name,
            RootNodeId = EntryNode?.Id ?? Nodes.FirstOrDefault()?.Id ?? string.Empty,
            Nodes = Nodes.Select(ToNodeDto).ToList(),
            Edges = Edges.Select(e => new EditorGraphEdgeDto
            {
                FromNodeId = e.From.Id,
                FromOutlet = e.Outlet,
                ToNodeId = e.To.Id
            }).ToList()
        };

        foreach (var node in document.Nodes.Where(n => string.Equals(n.Type, "Group", StringComparison.OrdinalIgnoreCase)))
            node.File = null;

        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(previewPath, "graph.json"), json);

        foreach (var group in Nodes.Where(n => n.NodeKind is GraphNodeKind.LinearGroup or GraphNodeKind.Entry))
            File.WriteAllLines(Path.Combine(previewPath, $"{group.Id}.galgroup"), group.Entries.Select(SerializeEntry));

        return previewPath;
    }
}
