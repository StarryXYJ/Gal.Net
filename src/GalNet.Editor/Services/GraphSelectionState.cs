using System.Collections.Generic;
using System.Collections.ObjectModel;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Services;

public sealed class GraphSelectionState
{
    public ObservableCollection<GraphNode> SelectedNodes { get; } = [];
    public GraphNode? SelectedNode { get; private set; }
    public GraphEdge? SelectedEdge { get; private set; }
    public bool HasMultipleNodes => SelectedNodes.Count > 1;

    public void SelectNode(GraphNode? node, bool additive = false)
    {
        if (!additive)
            Clear();

        if (node is not null && !SelectedNodes.Contains(node))
            SelectedNodes.Add(node);

        foreach (var selected in SelectedNodes)
            selected.IsSelected = true;

        SelectedNode = SelectedNodes.Count == 1 ? SelectedNodes[0] : null;
    }

    public void SelectNodes(IEnumerable<GraphNode> nodes)
    {
        Clear();

        foreach (var node in nodes)
        {
            if (SelectedNodes.Contains(node))
                continue;

            SelectedNodes.Add(node);
            node.IsSelected = true;
        }

        SelectedNode = SelectedNodes.Count == 1 ? SelectedNodes[0] : null;
    }

    public void SelectEdge(GraphEdge? edge)
    {
        Clear();
        SelectedEdge = edge;
        if (SelectedEdge is not null)
            SelectedEdge.IsSelected = true;
    }

    public void Clear()
    {
        foreach (var node in SelectedNodes)
            node.IsSelected = false;

        SelectedNodes.Clear();

        if (SelectedEdge is not null)
            SelectedEdge.IsSelected = false;

        SelectedNode = null;
        SelectedEdge = null;
    }
}
