using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Models.Graph;

namespace GalNet.Editor.Services;

/// <summary>Creates UI projections over project-owned author documents.</summary>
public sealed class GraphDocumentMapper
{
    public static GraphDocumentLoadResult Load(LoadedEditorProjectDocument loaded, EditorGroupProjectionStore? groups = null)
    {
        var nodes = loaded.Document.Nodes.Select(dto =>
        {
            var kind = dto.Type switch
            {
                "Entry" => GraphNodeKind.Entry,
                "Group" => GraphNodeKind.LinearGroup,
                "Branch" when dto.BranchType == "Choice" => GraphNodeKind.ChoiceBranch,
                "Branch" when dto.BranchType == "Condition" => GraphNodeKind.ConditionBranch,
                _ => throw new InvalidDataException($"Unknown graph node type '{dto.Type}/{dto.BranchType}'.")
            };
            var node = new GraphNode(dto, kind);
            if (kind == GraphNodeKind.LinearGroup)
            {
                if (groups is not null) node.BindGroup(groups.Get(dto.Id, dto.File));
                else if (loaded.GroupEntries.GetValueOrDefault(dto.Id) is { Count: > 0 })
                    throw new InvalidOperationException("Group content requires a project projection store.");
            }
            return node;
        }).ToList();
        var nodeMap = nodes.ToDictionary(node => node.Id);
        if (!nodeMap.TryGetValue(loaded.Document.RootNodeId, out var root) || !root.IsEntryNode)
            throw new InvalidDataException("The graph root must identify an explicit Entry node.");
        var edges = loaded.Document.Edges.Select(edge =>
        {
            if (!nodeMap.TryGetValue(edge.FromNodeId, out var from) || !nodeMap.TryGetValue(edge.ToNodeId, out var to)
                || edge.FromOutlet < 0 || edge.FromOutlet >= from.OutputConnectors.Count || to.InputConnectors.Count == 0)
                throw new InvalidDataException($"Invalid graph edge '{edge.Id}'.");
            return new GraphEdge(from, to, edge);
        }).ToList();
        return new GraphDocumentLoadResult(nodes, edges);
    }

    public static EditorGraphDocument CreateDocument(string projectName, int version, IEnumerable<GraphNode> nodes,
        IEnumerable<GraphEdge> edges, IEnumerable<GalNet.Core.Variable.ProjectVariableDefinition> playerVariables,
        IEnumerable<GalNet.Core.Variable.ProjectVariableDefinition> saveVariables) => new()
        {
            Version = version,
            Name = projectName,
            RootNodeId = nodes.FirstOrDefault(node => node.IsEntryNode)?.Id ?? "",
            Nodes = nodes.Select(node => node.Source).ToList(),
            Edges = edges.Select(edge => edge.Source).ToList(),
            PlayerVariables = playerVariables.Select(variable => variable.Clone()).ToList(),
            SaveVariables = saveVariables.Select(variable => variable.Clone()).ToList()
        };

    public static IReadOnlyDictionary<string, IReadOnlyList<EditorEntryData>> CreateGroupEntriesSnapshot(IEnumerable<GraphNode> nodes) =>
        nodes.Where(node => node.NodeKind == GraphNodeKind.LinearGroup).ToDictionary(node => node.Id,
            node => (IReadOnlyList<EditorEntryData>)node.Entries.Select(entry => new EditorEntryData
            {
                StableId = entry.StableId,
                Id = entry.Id,
                Type = entry.Type,
                Condition = entry.Condition,
                Parameters = entry.Source.Parameters.ToDictionary(pair => pair.Key, pair => pair.Value.Clone())
            }).ToList());
}

public sealed record GraphDocumentLoadResult(IReadOnlyList<GraphNode> Nodes, IReadOnlyList<GraphEdge> Edges);
