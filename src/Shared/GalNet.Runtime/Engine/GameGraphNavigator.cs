using GalNet.Core.Entry;
using GalNet.Core.Graph;

namespace GalNet.Runtime.Engine;

/// <summary>Indexes the compiled graph once; content topology is fixed for one Engine lifetime.</summary>
internal sealed class GameGraphNavigator
{
    private readonly Dictionary<string, Node> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<(string NodeId, int Outlet), string> _targets = [];
    public IEnumerable<Node> Nodes => _nodes.Values;
    public GameGraphNavigator(Graph graph)
    {
        foreach (var node in graph.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id) || !_nodes.TryAdd(node.Id, CopyNode(node)))
                throw new InvalidDataException($"Graph node '{node.Id}' must have a unique nonempty identity.");
        }
        if (!_nodes.ContainsKey(graph.RootNodeId)) throw new InvalidDataException("Graph root does not exist.");
        foreach (var edge in graph.Edges)
        {
            if (!_nodes.TryGetValue(edge.FromNodeId, out var from) || !_nodes.ContainsKey(edge.ToNodeId))
                throw new InvalidDataException("Graph edge references a missing node.");
            var outlets = from is Branch branch ? Math.Max(1, branch.BranchType == BranchType.Choice ? branch.Options.Count : branch.Conditions.Count) : 1;
            if (edge.FromOutlet < 0 || edge.FromOutlet >= outlets || !_targets.TryAdd((edge.FromNodeId, edge.FromOutlet), edge.ToNodeId))
                throw new InvalidDataException($"Graph outlet '{edge.FromNodeId}/{edge.FromOutlet}' is invalid or duplicated.");
        }
    }

    // Private copies are never exposed to authoring/host code. Runtime consumers only read them.
    private static Node CopyNode(Node node) => node switch
    {
        Group group => new Group { Id = group.Id, Name = group.Name, Entries = group.Entries.Select(CopyEntry).Cast<Entry>().ToList() },
        Branch branch when Enum.IsDefined(branch.BranchType) => new Branch
        {
            Id = branch.Id,
            Name = branch.Name,
            BranchType = branch.BranchType,
            Options = branch.Options.Select(option => new BranchOption { Text = option.Text, Condition = option.Condition }).ToList(),
            Conditions = branch.Conditions.Select(condition => new BranchCondition { Expression = condition.Expression }).ToList()
        },
        _ => throw new InvalidDataException($"Unsupported graph node '{node.Id}'.")
    };

    private static PrimitiveEntry CopyEntry(Entry entry) => entry is PrimitiveEntry primitive
        ? new(primitive.Type, primitive.Arguments, primitive.BatchId)
        {
            Id = primitive.Id,
            StableId = primitive.StableId,
            ReadingBoundaryId = primitive.ReadingBoundaryId,
            Condition = primitive.Condition
        }
        : throw new InvalidOperationException("The game graph must contain compiled primitives, not authoring entries.");
    public Node? FindNode(string nodeId) => _nodes.GetValueOrDefault(nodeId);
    public string? FindTarget(string nodeId, int outlet) => _targets.GetValueOrDefault((nodeId, outlet));
}
