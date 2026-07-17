using System;
using System.Collections.Generic;
using System.Linq;
using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Services;

/// <summary>Maps persisted graph data to the editor presentation model and back.</summary>
public sealed class GraphDocumentMapper
{
    public GraphDocumentLoadResult Load(LoadedEditorProjectDocument loaded)
    {
        var nodes = loaded.Document.Nodes
            .Select(dto => CreateNode(dto, loaded.GroupEntries))
            .ToList();
        var nodeMap = nodes.ToDictionary(node => node.Id);
        var entryEdge = EnsureEntryNode(loaded.Document, nodes, nodeMap);

        var edges = loaded.Document.Edges
            .Where(edge => nodeMap.TryGetValue(edge.FromNodeId, out var from)
                && nodeMap.TryGetValue(edge.ToNodeId, out var to)
                && edge.FromOutlet >= 0
                && edge.FromOutlet < from.OutputConnectors.Count
                && to.InputConnectors.Count > 0)
            .Select(edge => new GraphEdge(nodeMap[edge.FromNodeId], nodeMap[edge.ToNodeId], edge.FromOutlet, edge.Id))
            .ToList();
        if (entryEdge is not null)
            edges.Add(entryEdge);

        return new GraphDocumentLoadResult(nodes, edges);
    }

    public EditorGraphDocument CreateDocument(
        string projectName,
        int version,
        IEnumerable<GraphNode> nodes,
        IEnumerable<GraphEdge> edges,
        IEnumerable<GalNet.Core.Variable.ProjectVariableDefinition> playerVariables,
        IEnumerable<GalNet.Core.Variable.ProjectVariableDefinition> saveVariables) =>
        new()
        {
            Version = version <= 0 ? 2 : version,
            Name = projectName,
            RootNodeId = nodes.FirstOrDefault(node => node.NodeKind == GraphNodeKind.Entry)?.Id ?? nodes.FirstOrDefault()?.Id ?? "",
            Nodes = nodes.Select(ToDto).ToList(),
            Edges = edges.Select(edge => new EditorGraphEdgeDto { Id = edge.Id, FromNodeId = edge.From.Id, FromOutlet = edge.Outlet, ToNodeId = edge.To.Id }).ToList(),
            PlayerVariables = playerVariables.Select(variable => variable.Clone()).ToList(),
            SaveVariables = saveVariables.Select(variable => variable.Clone()).ToList()
        };

    public IReadOnlyDictionary<string, IReadOnlyList<EditorEntryData>> CreateGroupEntriesSnapshot(IEnumerable<GraphNode> nodes) =>
        nodes.Where(node => node.NodeKind == GraphNodeKind.LinearGroup)
            .ToDictionary(
                node => node.Id,
                node => (IReadOnlyList<EditorEntryData>)node.Entries.Select(entry => new EditorEntryData
                {
                    StableId = entry.StableId, Id = entry.Id, Type = entry.Type, Condition = entry.Condition, Parameters = entry.Parameters
                }).ToList());

    private static EditorGraphNodeDto ToDto(GraphNode node)
    {
        var dto = new EditorGraphNodeDto
        {
            Id = node.Id,
            Type = node.NodeKind switch { GraphNodeKind.Entry => "Entry", GraphNodeKind.LinearGroup => "Group", _ => "Branch" },
            Name = node.Name,
            X = node.X,
            Y = node.Y
        };
        if (node.NodeKind == GraphNodeKind.LinearGroup)
            dto.File = $"groups/{node.Id}.galgroup";
        else if (node.NodeKind is GraphNodeKind.ChoiceBranch or GraphNodeKind.ConditionBranch)
        {
            dto.BranchType = node.NodeKind == GraphNodeKind.ChoiceBranch ? "Choice" : "Condition";
            dto.Options = node.NodeKind == GraphNodeKind.ChoiceBranch
                ? node.Options.Select(option => new EditorGraphBranchOptionDto { Id = option.Id, Text = option.Text, Condition = option.Condition }).ToList()
                : null;
            dto.Conditions = node.NodeKind == GraphNodeKind.ConditionBranch
                ? node.Conditions.Select(condition => new EditorGraphBranchConditionDto { Id = condition.Id, Expression = condition.Expression }).ToList()
                : null;
        }
        return dto;
    }

    private static GraphNode CreateNode(EditorGraphNodeDto dto, IReadOnlyDictionary<string, List<EditorEntryData>> groupEntries)
    {
        var kind = dto.Type.Equals("Entry", StringComparison.OrdinalIgnoreCase) ? GraphNodeKind.Entry
            : dto.Type.Equals("Group", StringComparison.OrdinalIgnoreCase) ? GraphNodeKind.LinearGroup
            : dto.BranchType?.Equals("Condition", StringComparison.OrdinalIgnoreCase) == true ? GraphNodeKind.ConditionBranch
            : GraphNodeKind.ChoiceBranch;
        Node node = kind switch
        {
            GraphNodeKind.Entry => new Group { Id = dto.Id, Name = string.IsNullOrWhiteSpace(dto.Name) ? "Entry" : dto.Name },
            GraphNodeKind.LinearGroup => new Group { Id = dto.Id, Name = dto.Name },
            GraphNodeKind.ChoiceBranch => new Branch { Id = dto.Id, Name = dto.Name, BranchType = BranchType.Choice },
            GraphNodeKind.ConditionBranch => new Branch { Id = dto.Id, Name = dto.Name, BranchType = BranchType.Condition },
            _ => throw new ArgumentOutOfRangeException()
        };
        var graphNode = new GraphNode(node, kind) { X = dto.X, Y = dto.Y };
        if (kind == GraphNodeKind.LinearGroup)
        {
            graphNode.Entries.Clear();
            if (groupEntries.TryGetValue(dto.Id, out var entries))
                foreach (var entry in entries)
                    graphNode.Entries.Add(new EntryEditorItemViewModel { StableId = entry.StableId, Id = graphNode.Entries.Count + 1, Type = entry.Type, Condition = entry.Condition, Parameters = entry.Parameters });
        }
        if (kind == GraphNodeKind.ChoiceBranch)
        {
            graphNode.Options.Clear();
            foreach (var option in dto.Options ?? [])
                graphNode.Options.Add(new BranchOptionEditorItemViewModel { Id = option.Id, Text = option.Text, Condition = option.Condition });
        }
        if (kind == GraphNodeKind.ConditionBranch)
        {
            graphNode.Conditions.Clear();
            foreach (var condition in dto.Conditions ?? [])
                graphNode.Conditions.Add(new BranchConditionEditorItemViewModel { Id = condition.Id, Expression = condition.Expression });
        }
        graphNode.RefreshConnectors();
        return graphNode;
    }

    private static GraphEdge? EnsureEntryNode(EditorGraphDocument document, List<GraphNode> nodes, Dictionary<string, GraphNode> nodeMap)
    {
        if (nodes.Any(node => node.NodeKind == GraphNodeKind.Entry))
            return null;
        var root = !string.IsNullOrWhiteSpace(document.RootNodeId) && nodeMap.TryGetValue(document.RootNodeId, out var rootNode)
            ? rootNode : nodes.FirstOrDefault();
        var entry = new GraphNode(new Group { Name = "Entry" }, GraphNodeKind.Entry)
        {
            X = root is null ? 4420 : Math.Max(0, root.X - 280), Y = root?.Y ?? 4900, IsRoot = true
        };
        nodes.Insert(0, entry);
        nodeMap[entry.Id] = entry;
        return root is null ? null : new GraphEdge(entry, root);
    }
}

public sealed record GraphDocumentLoadResult(IReadOnlyList<GraphNode> Nodes, IReadOnlyList<GraphEdge> Edges);
