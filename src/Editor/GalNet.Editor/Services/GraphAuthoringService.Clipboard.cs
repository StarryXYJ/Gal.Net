using System.Text.Json;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;

namespace GalNet.Editor.Services;

public sealed partial class GraphAuthoringService
{
    private sealed record GraphClipboard(int Version, List<EditorGraphNodeDto> Nodes, List<EditorGraphEdgeDto> Edges, Dictionary<string, List<EditorEntryData>> Groups);

    public string CopyNodes(IEnumerable<string> nodeIds)
    {
        var ids = nodeIds.ToHashSet(StringComparer.Ordinal);
        var graph = documents.CurrentDocument;
        var nodes = graph.Nodes.Where(node => ids.Contains(node.Id) && node.Type != "Entry").ToList();
        if (nodes.Count == 0) return "";
        ids = nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        return JsonSerializer.Serialize(new GraphClipboard(1, nodes, graph.Edges.Where(edge => ids.Contains(edge.FromNodeId) && ids.Contains(edge.ToNodeId)).ToList(),
            documents.LoadedProject.GroupEntries.Where(pair => ids.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value)));
    }

    public async Task<IReadOnlyList<string>> PasteAsync(string text, double x, double y)
    {
        GraphClipboard clip;
        try
        {
            if (text.Length > 16 * 1024 * 1024 || !double.IsFinite(x) || !double.IsFinite(y)) throw new JsonException();
            clip = JsonSerializer.Deserialize<GraphClipboard>(text) ?? throw new JsonException();
            if (clip.Version != 1 || clip.Nodes is not { Count: > 0 and <= 1000 } || clip.Edges is null || clip.Groups is null
                || clip.Nodes.Any(node => node is null || string.IsNullOrWhiteSpace(node.Id) || !double.IsFinite(node.X) || !double.IsFinite(node.Y)
                    || node.Type is not ("Group" or "Branch") || node.Type == "Branch" && node.BranchType is not ("Choice" or "Condition"))
                || clip.Nodes.Select(node => node.Id).Distinct(StringComparer.Ordinal).Count() != clip.Nodes.Count) throw new JsonException();
            var nodes = clip.Nodes.ToDictionary(node => node.Id);
            foreach (var node in clip.Nodes)
            {
                if (node.Type == "Group" && (!clip.Groups.TryGetValue(node.Id, out var entries) || entries is null || entries.Any(entry => entry is null || entry.Parameters is null))) throw new JsonException();
                var slots = node.BranchType == "Choice" ? node.Options?.Select(item => item.Id).ToArray() : node.BranchType == "Condition" ? node.Conditions?.Select(item => item.Id).ToArray() : [];
                if (slots is null || slots.Any(string.IsNullOrWhiteSpace) || slots.Distinct(StringComparer.Ordinal).Count() != slots.Length) throw new JsonException();
            }
            if (clip.Edges.Any(edge => edge is null || !nodes.ContainsKey(edge.FromNodeId) || !nodes.ContainsKey(edge.ToNodeId) || edge.FromNodeId == edge.ToNodeId
                || edge.FromOutlet < 0 || edge.FromOutlet >= OutletCount(nodes[edge.FromNodeId]))
                || clip.Edges.GroupBy(edge => (edge.FromNodeId, edge.FromOutlet)).Any(group => group.Count() > 1)) throw new JsonException();
        }
        catch (Exception failure) when (failure is JsonException or ArgumentException or NullReferenceException)
        { throw new EditorOperationException("graph.clipboard", "The clipboard does not contain a supported GalNet graph selection."); }

        var map = clip.Nodes.ToDictionary(node => node.Id, _ => Guid.NewGuid().ToString("N"));
        var minX = clip.Nodes.Min(node => node.X); var minY = clip.Nodes.Min(node => node.Y);
        var groups = new Dictionary<string, List<EditorEntryData>>();
        foreach (var node in clip.Nodes)
        {
            var originalId = node.Id;
            node.Id = map[originalId];
            node.X = Math.Max(0, x + node.X - minX); node.Y = Math.Max(0, y + node.Y - minY);
            foreach (var option in node.Options ?? []) option.Id = Guid.NewGuid().ToString("N");
            foreach (var condition in node.Conditions ?? []) condition.Id = Guid.NewGuid().ToString("N");
            if (node.Type != "Group") continue;
            node.File = $"groups/{node.Id}.rawgalgroup";
            var entries = clip.Groups[originalId];
            for (var index = 0; index < entries.Count; index++) { entries[index].StableId = Guid.NewGuid().ToString("N"); entries[index].Id = index + 1; }
            groups.Add(node.Id, entries);
        }
        foreach (var edge in clip.Edges) { edge.Id = Guid.NewGuid().ToString("N"); edge.FromNodeId = map[edge.FromNodeId]; edge.ToNodeId = map[edge.ToNodeId]; }
        var bundle = new Bundle(clip.Nodes.Select((node, index) => (documents.CurrentDocument.Nodes.Count + index, node)).ToArray(),
            clip.Edges.Select((edge, index) => (documents.CurrentDocument.Edges.Count + index, edge)).ToArray(), groups);
        var ids = clip.Nodes.Select(node => node.Id).ToArray();
        await histories.Content.ExecuteAsync(new PresenceEdit(documents, content, ids, bundle, display: new EditorText("History.PasteNodes", ids.Length)));
        return ids;
    }

    private static int OutletCount(EditorGraphNodeDto node) => node.BranchType == "Choice" ? node.Options?.Count ?? 0 : node.BranchType == "Condition" ? node.Conditions?.Count ?? 0 : 1;
}
