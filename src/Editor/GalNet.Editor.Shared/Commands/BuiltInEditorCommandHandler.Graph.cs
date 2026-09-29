using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Documents;

namespace GalNet.Editor.Shared.Commands;

public sealed partial class BuiltInEditorCommandHandler
{
    private static CommandExecution CreateNode(EditorProjectDocument document, CreateNodeCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.NodeId))
            return Error("graph.node.idRequired", "Node ID is required.");
        if (FindNode(document, command.NodeId) is not null)
            return Error("graph.node.duplicateId", $"Node '{command.NodeId}' already exists.", NodeResource(command.NodeId));
        if (!double.IsFinite(command.X) || !double.IsFinite(command.Y))
            return Error("graph.node.invalidPosition", "Node coordinates must be finite numbers.");
        if (command.Kind == EditorNodeKind.Entry && document.Graph.Nodes.Any(IsEntryNode))
            return Error("graph.node.entryExists", "The graph already contains an entry node.");

        var node = new EditorGraphNodeDto
        {
            Id = command.NodeId,
            Type = command.Kind switch
            {
                EditorNodeKind.Entry => "Entry",
                EditorNodeKind.LinearGroup => "Group",
                _ => "Branch"
            },
            BranchType = command.Kind switch
            {
                EditorNodeKind.ChoiceBranch => "Choice",
                EditorNodeKind.ConditionBranch => "Condition",
                _ => null
            },
            Name = string.IsNullOrWhiteSpace(command.Name) ? command.NodeId : command.Name.Trim(),
            X = command.X,
            Y = command.Y,
            File = command.Kind == EditorNodeKind.LinearGroup ? $"groups/{command.NodeId}.rawgalgroup" : null,
            Options = command.Kind == EditorNodeKind.ChoiceBranch ? [] : null,
            Conditions = command.Kind == EditorNodeKind.ConditionBranch ? [] : null
        };
        document.Graph.Nodes.Add(node);
        if (command.Kind == EditorNodeKind.LinearGroup)
            document.GroupEntries[command.NodeId] = [];
        if (command.Kind == EditorNodeKind.Entry || string.IsNullOrWhiteSpace(document.Graph.RootNodeId))
            document.Graph.RootNodeId = command.NodeId;

        return Success($"Created node '{node.Name}'.", "History.Graph.CreateNode", NodeResource(node.Id), node.Name);
    }

    private static CommandExecution DeleteNode(EditorProjectDocument document, DeleteNodeCommand command)
    {
        var node = FindNode(document, command.NodeId);
        if (node is null)
            return NotFound("graph.node.notFound", "Node", command.NodeId, NodeResource(command.NodeId));
        if (IsEntryNode(node))
            return Error("graph.node.entryDeleteDenied", "The entry node cannot be deleted.", NodeResource(command.NodeId));

        var edgeCount = document.Graph.Edges.RemoveAll(edge =>
            edge.FromNodeId == node.Id || edge.ToNodeId == node.Id);
        document.Graph.Nodes.Remove(node);
        if (command.DeleteGroupEntries)
            document.GroupEntries.Remove(node.Id);
        if (document.Graph.RootNodeId == node.Id)
            document.Graph.RootNodeId = document.Graph.Nodes.FirstOrDefault(IsEntryNode)?.Id
                ?? document.Graph.Nodes.FirstOrDefault()?.Id ?? "";

        return Success(
            $"Deleted node '{node.Name}' and {edgeCount} connected edge(s).",
            "History.Graph.DeleteNode",
            NodeResource(node.Id),
            node.Name);
    }

    private static CommandExecution RenameNode(EditorProjectDocument document, RenameNodeCommand command)
    {
        var node = FindNode(document, command.NodeId);
        if (node is null)
            return NotFound("graph.node.notFound", "Node", command.NodeId, NodeResource(command.NodeId));
        if (string.IsNullOrWhiteSpace(command.Name))
            return Error("graph.node.nameRequired", "Node name is required.", NodeResource(command.NodeId));
        var oldName = node.Name;
        node.Name = command.Name.Trim();
        return Success($"Renamed node '{oldName}' to '{node.Name}'.", "History.Graph.RenameNode", NodeResource(node.Id), oldName, node.Name);
    }

    private static CommandExecution MoveNodes(EditorProjectDocument document, MoveNodesCommand command)
    {
        if (command.Nodes.Count == 0)
            return Error("graph.node.emptyMove", "At least one node position is required.");
        if (command.Nodes.Select(item => item.NodeId).Distinct(StringComparer.Ordinal).Count() != command.Nodes.Count)
            return Error("graph.node.duplicateMove", "A node can only appear once in a move command.");

        foreach (var position in command.Nodes)
        {
            var node = FindNode(document, position.NodeId);
            if (node is null)
                return NotFound("graph.node.notFound", "Node", position.NodeId, NodeResource(position.NodeId));
            if (!double.IsFinite(position.X) || !double.IsFinite(position.Y))
                return Error("graph.node.invalidPosition", $"Coordinates for node '{position.NodeId}' must be finite.");
        }
        foreach (var position in command.Nodes)
        {
            var node = FindNode(document, position.NodeId)!;
            node.X = position.X;
            node.Y = position.Y;
        }
        return Success(
            $"Moved {command.Nodes.Count} node(s).",
            "History.Graph.MoveNodes",
            command.Nodes.Select(item => NodeResource(item.NodeId)).ToArray(),
            command.Nodes.Count);
    }

    private static CommandExecution SetRoot(EditorProjectDocument document, SetRootNodeCommand command)
    {
        if (FindNode(document, command.NodeId) is null)
            return NotFound("graph.node.notFound", "Node", command.NodeId, NodeResource(command.NodeId));
        document.Graph.RootNodeId = command.NodeId;
        return Success($"Set graph root node to '{command.NodeId}'.", "History.Graph.SetRoot", NodeResource(command.NodeId), command.NodeId);
    }

    private static CommandExecution Connect(EditorProjectDocument document, ConnectNodesCommand command)
    {
        var from = FindNode(document, command.FromNodeId);
        var to = FindNode(document, command.ToNodeId);
        if (from is null)
            return NotFound("graph.node.notFound", "Node", command.FromNodeId, NodeResource(command.FromNodeId));
        if (to is null)
            return NotFound("graph.node.notFound", "Node", command.ToNodeId, NodeResource(command.ToNodeId));
        if (from.Id == to.Id)
            return Error("graph.edge.selfConnection", "A node cannot connect to itself.");
        if (IsEntryNode(to))
            return Error("graph.edge.entryInput", "The entry node cannot have an incoming edge.", NodeResource(to.Id));
        if (command.Outlet < 0 || command.Outlet >= OutputCount(from))
            return Error("graph.edge.invalidOutlet", $"Outlet {command.Outlet} does not exist on node '{from.Id}'.", NodeResource(from.Id));

        document.Graph.Edges.RemoveAll(edge =>
            edge.ToNodeId == to.Id ||
            (edge.FromNodeId == from.Id && edge.FromOutlet == command.Outlet));
        var edge = new EditorGraphEdgeDto
        {
            Id = string.IsNullOrWhiteSpace(command.EdgeId) ? Guid.NewGuid().ToString("N") : command.EdgeId,
            FromNodeId = from.Id,
            FromOutlet = command.Outlet,
            ToNodeId = to.Id
        };
        if (document.Graph.Edges.Any(item => item.Id == edge.Id))
            return Error("graph.edge.duplicateId", $"Edge '{edge.Id}' already exists.");
        document.Graph.Edges.Add(edge);
        return Success(
            $"Connected '{from.Id}' outlet {command.Outlet} to '{to.Id}'.",
            "History.Graph.ConnectNodes",
            EdgeResource(edge.Id),
            from.Name,
            to.Name);
    }

    private static CommandExecution DeleteEdge(EditorProjectDocument document, DeleteEdgeCommand command)
    {
        var edge = !string.IsNullOrWhiteSpace(command.EdgeId)
            ? document.Graph.Edges.FirstOrDefault(item => item.Id == command.EdgeId)
            : document.Graph.Edges.FirstOrDefault(item =>
                item.FromNodeId == command.FromNodeId &&
                item.FromOutlet == command.Outlet &&
                item.ToNodeId == command.ToNodeId);
        if (edge is null)
            return Error("graph.edge.notFound", "The requested graph edge does not exist.");
        document.Graph.Edges.Remove(edge);
        return Success(
            $"Deleted edge from '{edge.FromNodeId}' outlet {edge.FromOutlet} to '{edge.ToNodeId}'.",
            "History.Graph.DeleteEdge",
            EdgeResource(edge.Id),
            edge.FromNodeId,
            edge.ToNodeId);
    }

    private static EditorGraphNodeDto? FindNode(EditorProjectDocument document, string nodeId) =>
        document.Graph.Nodes.FirstOrDefault(node => node.Id == nodeId);

    private static bool IsEntryNode(EditorGraphNodeDto node) =>
        node.Type.Equals("Entry", StringComparison.OrdinalIgnoreCase);

    private static bool IsGroupNode(EditorGraphNodeDto node) =>
        node.Type.Equals("Group", StringComparison.OrdinalIgnoreCase);

    private static bool IsChoiceNode(EditorGraphNodeDto node) =>
        node.Type.Equals("Branch", StringComparison.OrdinalIgnoreCase) &&
        node.BranchType?.Equals("Choice", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsConditionNode(EditorGraphNodeDto node) =>
        node.Type.Equals("Branch", StringComparison.OrdinalIgnoreCase) &&
        node.BranchType?.Equals("Condition", StringComparison.OrdinalIgnoreCase) == true;

    private static int OutputCount(EditorGraphNodeDto node)
    {
        if (IsChoiceNode(node)) return Math.Max(1, node.Options?.Count ?? 0);
        if (IsConditionNode(node)) return Math.Max(1, node.Conditions?.Count ?? 0);
        return 1;
    }

}
