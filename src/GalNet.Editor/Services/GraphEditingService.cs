using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using GalNet.Core.Graph;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Services;

public sealed class GraphEditingService : IGraphEditingService
{
    public GraphNode CreateNode(ObservableCollection<GraphNode> nodes, GraphNodeKind kind, double x, double y, string? id = null)
    {
        var index = nodes.Count(node => node.NodeKind == kind) + 1;
        Node node = kind switch
        {
            GraphNodeKind.LinearGroup => new Group { Id = id ?? Guid.NewGuid().ToString("N"), Name = $"Linear Group {index}" },
            GraphNodeKind.ChoiceBranch => new Branch { Id = id ?? Guid.NewGuid().ToString("N"), Name = $"Choice Branch {index}", BranchType = BranchType.Choice },
            GraphNodeKind.ConditionBranch => new Branch { Id = id ?? Guid.NewGuid().ToString("N"), Name = $"Condition Branch {index}", BranchType = BranchType.Condition },
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

        return new GraphNode(node, kind)
        {
            X = x,
            Y = y
        };
    }

    public bool DeleteEdge(ObservableCollection<GraphNode> nodes, ObservableCollection<GraphEdge> edges, GraphEdge edge)
    {
        var removed = edges.Remove(edge);
        if (removed)
            UpdateConnectorStates(nodes, edges);

        return removed;
    }

    public bool DeleteNode(ObservableCollection<GraphNode> nodes, ObservableCollection<GraphEdge> edges, GraphNode node)
    {
        if (!node.CanDelete)
            return false;

        var relatedEdges = edges
            .Where(edge => ReferenceEquals(edge.From, node) || ReferenceEquals(edge.To, node))
            .ToList();

        foreach (var edge in relatedEdges)
            edges.Remove(edge);

        var removed = nodes.Remove(node);
        if (removed)
            UpdateConnectorStates(nodes, edges);

        return removed;
    }

    public bool Connect(ObservableCollection<GraphNode> nodes, ObservableCollection<GraphEdge> edges, GraphConnector first, GraphConnector second)
    {
        if (ReferenceEquals(first.Node, second.Node) || first.Kind == second.Kind)
            return false;

        var output = first.Kind == GraphConnectorKind.Output ? first : second;
        var input = first.Kind == GraphConnectorKind.Input ? first : second;

        var conflictingEdges = edges
            .Where(edge =>
                ReferenceEquals(edge.To, input.Node)
                || (ReferenceEquals(edge.From, output.Node) && edge.Outlet == output.Index))
            .ToList();

        foreach (var edge in conflictingEdges)
            edges.Remove(edge);

        edges.Add(new GraphEdge(output.Node, input.Node, output.Index));
        UpdateConnectorStates(nodes, edges);
        return true;
    }

    public IReadOnlyList<EntryEditorItemViewModel> InsertEntries(GraphNode groupNode, int index, int count)
    {
        if (groupNode.NodeKind != GraphNodeKind.LinearGroup || count < 1)
            return [];

        index = Math.Clamp(index, 0, groupNode.Entries.Count);
        var inserted = new List<EntryEditorItemViewModel>(count);
        for (var offset = 0; offset < count; offset++)
        {
            var entry = new EntryEditorItemViewModel
            {
                StableId = Guid.NewGuid().ToString("N"),
                Type = GalNet.Core.Entry.TextEntry.TypeId,
                Parameters = new Dictionary<string, string>(
                    GalNet.Core.Entry.EntryRegistry.Create(GalNet.Core.Entry.TextEntry.TypeId).Values,
                    StringComparer.Ordinal)
            };
            groupNode.Entries.Insert(index + offset, entry);
            inserted.Add(entry);
        }
        RenumberEntries(groupNode);
        return inserted;
    }

    public bool RemoveEntry(GraphNode groupNode, EntryEditorItemViewModel entry)
    {
        var removed = groupNode.Entries.Remove(entry);
        if (removed)
            RenumberEntries(groupNode);

        return removed;
    }

    public bool MoveEntry(GraphNode groupNode, EntryEditorItemViewModel entry, int newIndex)
    {
        var oldIndex = groupNode.Entries.IndexOf(entry);
        if (oldIndex < 0 || newIndex < 0 || newIndex >= groupNode.Entries.Count || oldIndex == newIndex)
            return false;

        groupNode.Entries.Move(oldIndex, newIndex);
        RenumberEntries(groupNode);
        return true;
    }

    public bool AddChoiceOption(ObservableCollection<GraphNode> nodes, ObservableCollection<GraphEdge> edges, GraphNode node)
    {
        if (node.NodeKind != GraphNodeKind.ChoiceBranch)
            return false;

        node.Options.Add(new BranchOptionEditorItemViewModel
        {
            Id = Guid.NewGuid().ToString("N"),
            Text = $"Option {node.Options.Count + 1}"
        });
        node.RefreshConnectors();
        UpdateConnectorStates(nodes, edges);
        return true;
    }

    public bool RemoveChoiceOption(ObservableCollection<GraphNode> nodes, ObservableCollection<GraphEdge> edges, GraphNode node, BranchOptionEditorItemViewModel option)
    {
        if (node.NodeKind != GraphNodeKind.ChoiceBranch || !node.Options.Remove(option))
            return false;

        node.RefreshConnectors();
        RemoveDanglingOutletEdges(node, edges);
        UpdateConnectorStates(nodes, edges);
        return true;
    }

    public bool MoveChoiceOption(ObservableCollection<GraphNode> nodes, ObservableCollection<GraphEdge> edges, GraphNode node, BranchOptionEditorItemViewModel option, int newIndex)
    {
        if (node.NodeKind != GraphNodeKind.ChoiceBranch)
            return false;

        return MoveBranchItemWithEdges(nodes, edges, node, node.Options, option, newIndex);
    }

    public bool AddCondition(ObservableCollection<GraphNode> nodes, ObservableCollection<GraphEdge> edges, GraphNode node)
    {
        if (node.NodeKind != GraphNodeKind.ConditionBranch)
            return false;

        node.Conditions.Add(new BranchConditionEditorItemViewModel
        {
            Id = Guid.NewGuid().ToString("N"),
            Expression = "true"
        });
        node.RefreshConnectors();
        UpdateConnectorStates(nodes, edges);
        return true;
    }

    public bool RemoveCondition(ObservableCollection<GraphNode> nodes, ObservableCollection<GraphEdge> edges, GraphNode node, BranchConditionEditorItemViewModel condition)
    {
        if (node.NodeKind != GraphNodeKind.ConditionBranch || !node.Conditions.Remove(condition))
            return false;

        node.RefreshConnectors();
        RemoveDanglingOutletEdges(node, edges);
        UpdateConnectorStates(nodes, edges);
        return true;
    }

    public bool MoveCondition(ObservableCollection<GraphNode> nodes, ObservableCollection<GraphEdge> edges, GraphNode node, BranchConditionEditorItemViewModel condition, int newIndex)
    {
        if (node.NodeKind != GraphNodeKind.ConditionBranch)
            return false;

        return MoveBranchItemWithEdges(nodes, edges, node, node.Conditions, condition, newIndex);
    }

    public void UpdateConnectorStates(ObservableCollection<GraphNode> nodes, ObservableCollection<GraphEdge> edges)
    {
        foreach (var connector in nodes.SelectMany(node => node.InputConnectors.Concat(node.OutputConnectors)))
            connector.IsConnected = false;

        foreach (var edge in edges)
        {
            var output = edge.From.OutputConnectors.FirstOrDefault(connector => connector.Index == edge.Outlet);
            if (output is not null)
                output.IsConnected = true;

            var input = edge.To.InputConnectors.FirstOrDefault();
            if (input is not null)
                input.IsConnected = true;
        }
    }

    private static void RenumberEntries(GraphNode groupNode)
    {
        for (var index = 0; index < groupNode.Entries.Count; index++)
            groupNode.Entries[index].Id = index + 1;
    }

    private static void RemoveDanglingOutletEdges(GraphNode node, ObservableCollection<GraphEdge> edges)
    {
        var maxOutlet = node.OutputConnectors.Count - 1;
        var danglingEdges = edges
            .Where(edge => ReferenceEquals(edge.From, node) && edge.Outlet > maxOutlet)
            .ToList();

        foreach (var edge in danglingEdges)
            edges.Remove(edge);
    }

    private bool MoveBranchItemWithEdges<TItem>(
        ObservableCollection<GraphNode> nodes,
        ObservableCollection<GraphEdge> edges,
        GraphNode node,
        ObservableCollection<TItem> items,
        TItem item,
        int newIndex)
        where TItem : class
    {
        var oldIndex = items.IndexOf(item);
        if (oldIndex < 0 || newIndex < 0 || newIndex >= items.Count || oldIndex == newIndex)
            return false;

        var oldOrder = items.ToList();
        var edgeByItem = oldOrder
            .Select((entry, index) => new { entry, edge = edges.FirstOrDefault(current => ReferenceEquals(current.From, node) && current.Outlet == index) })
            .Where(pair => pair.edge is not null)
            .ToDictionary(pair => pair.entry, pair => pair.edge!);

        items.Move(oldIndex, newIndex);

        for (var index = 0; index < items.Count; index++)
        {
            if (edgeByItem.TryGetValue(items[index], out var edge))
                edge.Outlet = index;
        }

        node.RefreshConnectors();
        UpdateConnectorStates(nodes, edges);
        return true;
    }
}
