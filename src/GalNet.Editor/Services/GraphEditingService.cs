using System;
using System.Collections.ObjectModel;
using System.Linq;
using GalNet.Core.Graph;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Services;

public sealed class GraphEditingService : IGraphEditingService
{
    public GraphNodeViewModel CreateNode(ObservableCollection<GraphNodeViewModel> nodes, GraphNodeKind kind, double x, double y)
    {
        var index = nodes.Count(node => node.NodeKind == kind) + 1;
        Node node = kind switch
        {
            GraphNodeKind.LinearGroup => new Group { Name = $"Linear Group {index}" },
            GraphNodeKind.ChoiceBranch => new Branch { Name = $"Choice Branch {index}", BranchType = BranchType.Choice },
            GraphNodeKind.ConditionBranch => new Branch { Name = $"Condition Branch {index}", BranchType = BranchType.Condition },
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

        return new GraphNodeViewModel(node, kind)
        {
            X = x,
            Y = y
        };
    }

    public bool DeleteEdge(ObservableCollection<GraphNodeViewModel> nodes, ObservableCollection<GraphEdgeViewModel> edges, GraphEdgeViewModel edge)
    {
        var removed = edges.Remove(edge);
        if (removed)
            UpdateConnectorStates(nodes, edges);

        return removed;
    }

    public bool DeleteNode(ObservableCollection<GraphNodeViewModel> nodes, ObservableCollection<GraphEdgeViewModel> edges, GraphNodeViewModel node)
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

    public bool Connect(ObservableCollection<GraphNodeViewModel> nodes, ObservableCollection<GraphEdgeViewModel> edges, GraphConnectorViewModel first, GraphConnectorViewModel second)
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

        edges.Add(new GraphEdgeViewModel(output.Node, input.Node, output.Index));
        UpdateConnectorStates(nodes, edges);
        return true;
    }

    public bool AddEntry(GraphNodeViewModel groupNode)
    {
        if (groupNode.NodeKind != GraphNodeKind.LinearGroup)
            return false;

        groupNode.Entries.Add(new EntryEditorItemViewModel
        {
            Id = groupNode.Entries.Count + 1,
            Type = "text",
            Parameters = "speaker=; text="
        });
        return true;
    }

    public bool RemoveEntry(GraphNodeViewModel groupNode, EntryEditorItemViewModel entry)
    {
        var removed = groupNode.Entries.Remove(entry);
        if (removed)
            RenumberEntries(groupNode);

        return removed;
    }

    public bool MoveEntry(GraphNodeViewModel groupNode, EntryEditorItemViewModel entry, int newIndex)
    {
        var oldIndex = groupNode.Entries.IndexOf(entry);
        if (oldIndex < 0 || newIndex < 0 || newIndex >= groupNode.Entries.Count || oldIndex == newIndex)
            return false;

        groupNode.Entries.Move(oldIndex, newIndex);
        RenumberEntries(groupNode);
        return true;
    }

    public bool AddChoiceOption(ObservableCollection<GraphNodeViewModel> nodes, ObservableCollection<GraphEdgeViewModel> edges, GraphNodeViewModel node)
    {
        if (node.NodeKind != GraphNodeKind.ChoiceBranch)
            return false;

        node.Options.Add(new BranchOptionEditorItemViewModel
        {
            Text = $"Option {node.Options.Count + 1}"
        });
        node.RefreshConnectors();
        UpdateConnectorStates(nodes, edges);
        return true;
    }

    public bool RemoveChoiceOption(ObservableCollection<GraphNodeViewModel> nodes, ObservableCollection<GraphEdgeViewModel> edges, GraphNodeViewModel node, BranchOptionEditorItemViewModel option)
    {
        if (node.NodeKind != GraphNodeKind.ChoiceBranch || !node.Options.Remove(option))
            return false;

        node.RefreshConnectors();
        RemoveDanglingOutletEdges(node, edges);
        UpdateConnectorStates(nodes, edges);
        return true;
    }

    public bool MoveChoiceOption(ObservableCollection<GraphNodeViewModel> nodes, ObservableCollection<GraphEdgeViewModel> edges, GraphNodeViewModel node, BranchOptionEditorItemViewModel option, int newIndex)
    {
        if (node.NodeKind != GraphNodeKind.ChoiceBranch)
            return false;

        return MoveBranchItemWithEdges(nodes, edges, node, node.Options, option, newIndex);
    }

    public bool AddCondition(ObservableCollection<GraphNodeViewModel> nodes, ObservableCollection<GraphEdgeViewModel> edges, GraphNodeViewModel node)
    {
        if (node.NodeKind != GraphNodeKind.ConditionBranch)
            return false;

        node.Conditions.Add(new BranchConditionEditorItemViewModel
        {
            Expression = "true"
        });
        node.RefreshConnectors();
        UpdateConnectorStates(nodes, edges);
        return true;
    }

    public bool RemoveCondition(ObservableCollection<GraphNodeViewModel> nodes, ObservableCollection<GraphEdgeViewModel> edges, GraphNodeViewModel node, BranchConditionEditorItemViewModel condition)
    {
        if (node.NodeKind != GraphNodeKind.ConditionBranch || !node.Conditions.Remove(condition))
            return false;

        node.RefreshConnectors();
        RemoveDanglingOutletEdges(node, edges);
        UpdateConnectorStates(nodes, edges);
        return true;
    }

    public bool MoveCondition(ObservableCollection<GraphNodeViewModel> nodes, ObservableCollection<GraphEdgeViewModel> edges, GraphNodeViewModel node, BranchConditionEditorItemViewModel condition, int newIndex)
    {
        if (node.NodeKind != GraphNodeKind.ConditionBranch)
            return false;

        return MoveBranchItemWithEdges(nodes, edges, node, node.Conditions, condition, newIndex);
    }

    public void UpdateConnectorStates(ObservableCollection<GraphNodeViewModel> nodes, ObservableCollection<GraphEdgeViewModel> edges)
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

    private static void RenumberEntries(GraphNodeViewModel groupNode)
    {
        for (var index = 0; index < groupNode.Entries.Count; index++)
            groupNode.Entries[index].Id = index + 1;
    }

    private static void RemoveDanglingOutletEdges(GraphNodeViewModel node, ObservableCollection<GraphEdgeViewModel> edges)
    {
        var maxOutlet = node.OutputConnectors.Count - 1;
        var danglingEdges = edges
            .Where(edge => ReferenceEquals(edge.From, node) && edge.Outlet > maxOutlet)
            .ToList();

        foreach (var edge in danglingEdges)
            edges.Remove(edge);
    }

    private bool MoveBranchItemWithEdges<TItem>(
        ObservableCollection<GraphNodeViewModel> nodes,
        ObservableCollection<GraphEdgeViewModel> edges,
        GraphNodeViewModel node,
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
