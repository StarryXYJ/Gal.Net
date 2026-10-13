using GalNet.Core.Graph;

namespace GalNet.Runtime.Engine;

/// <summary>Indexes the compiled graph once; content topology is fixed for one Engine lifetime.</summary>
internal sealed class GameGraphNavigator
{
    private readonly Dictionary<string, Node> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<(string NodeId, int Outlet), string> _targets = [];
    public GameGraphNavigator(Graph graph)
    {
        foreach (var node in graph.Nodes) _nodes.TryAdd(node.Id, node);
        foreach (var edge in graph.Edges) _targets.TryAdd((edge.FromNodeId, edge.FromOutlet), edge.ToNodeId);
    }
    public Node? FindNode(string nodeId) => _nodes.GetValueOrDefault(nodeId);
    public string? FindTarget(string nodeId, int outlet) => _targets.GetValueOrDefault((nodeId, outlet));
}
