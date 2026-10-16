using System.Text.Json;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;

namespace GalNet.Editor.Services;

public sealed partial class GraphAuthoringService
{
    public void AddBranchItem(string nodeId)
    {
        var node = Find(nodeId);
        ChangeBranch(node, "History.AddBranchItem", state =>
        {
            if (node.BranchType == "Choice") state.Options!.Add(new() { Id = Guid.NewGuid().ToString("N"), Text = $"Option {state.Options.Count + 1}" });
            else state.Conditions!.Add(new() { Id = Guid.NewGuid().ToString("N"), Expression = "true" });
        });
    }

    public void RemoveBranchItem(string nodeId, string itemId) => ReorderBranch(nodeId, itemId, null);
    public void MoveBranchItem(string nodeId, string itemId, int index) => ReorderBranch(nodeId, itemId, index);

    private void ReorderBranch(string nodeId, string itemId, int? index)
    {
        var node = Find(nodeId);
        ChangeBranch(node, index is null ? "History.RemoveBranchItem" : "History.MoveBranchItem", state =>
        {
            var order = state.Ids;
            var oldIndex = Array.IndexOf(order, itemId);
            if (oldIndex < 0 || index is < 0 || index >= order.Length) throw BranchChanged();
            var targets = state.Edges.Select((edge, index) => (edge, Index: state.EdgeIndices[index], Slot: order.ElementAtOrDefault(edge.FromOutlet))).ToArray();
            if (state.Options is { } options) { var item = options[oldIndex]; options.RemoveAt(oldIndex); if (index is { } next) options.Insert(next, item); }
            else { var conditions = state.Conditions!; var item = conditions[oldIndex]; conditions.RemoveAt(oldIndex); if (index is { } next) conditions.Insert(next, item); }
            var nextOrder = state.Ids;
            state.Edges.Clear(); state.EdgeIndices.Clear();
            foreach (var (edge, edgeIndex, slot) in targets)
            {
                var outlet = Array.IndexOf(nextOrder, slot);
                if (outlet < 0) continue;
                edge.FromOutlet = outlet;
                state.Edges.Add(edge); state.EdgeIndices.Add(edgeIndex - targets.Count(item => item.Index < edgeIndex && !nextOrder.Contains(item.Slot)));
            }
        });
    }

    public void SetBranchValue(string nodeId, string itemId, string field, string? value)
    {
        var node = Find(nodeId);
        ChangeBranch(node, "History.EditBranchItem", state =>
        {
            if (state.Options is { } options)
            {
                var item = options.Single(item => item.Id == itemId);
                if (field == "Text") item.Text = value ?? "";
                else if (field == "Condition") item.Condition = value ?? "";
                else throw BranchChanged();
            }
            else if (field == "Expression") state.Conditions!.Single(item => item.Id == itemId).Expression = value ?? "";
            else throw BranchChanged();
        });
    }

    private void ChangeBranch(EditorGraphNodeDto node, string key, Action<BranchState> update)
    {
        if (node.Type != "Branch" || node.BranchType is not ("Choice" or "Condition")) throw BranchChanged();
        var before = BranchState.Capture(node, documents.CurrentDocument);
        var after = Copy(before);
        update(after);
        if (JsonSerializer.Serialize(before) == JsonSerializer.Serialize(after)) return;
        histories.Content.Execute(new BranchEdit(documents, node.Id, node.Name, key, before, after));
    }

    private static EditorOperationException BranchChanged() => new("graph.branchChanged", "The branch or its items changed. Review the current values and retry.");
    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value))!;

    private sealed record BranchState(List<EditorGraphBranchOptionDto>? Options, List<EditorGraphBranchConditionDto>? Conditions, List<EditorGraphEdgeDto> Edges, List<int> EdgeIndices)
    {
        [System.Text.Json.Serialization.JsonIgnore]
        public string[] Ids => Options?.Select(item => item.Id).ToArray() ?? Conditions!.Select(item => item.Id).ToArray();
        public static BranchState Capture(EditorGraphNodeDto node, EditorGraphDocument graph) => Copy(new BranchState(node.Options, node.Conditions, graph.Edges.Where(edge => edge.FromNodeId == node.Id).ToList(), graph.Edges.Select((edge, index) => (edge, index)).Where(item => item.edge.FromNodeId == node.Id).Select(item => item.index).ToList()));
    }

    private sealed record BranchEdit(IEditorDocumentService Source, string NodeId, string TargetName, string Key, BranchState Before, BranchState After) : IUndoableEdit
    {
        public string Description => "Edit branch " + NodeId;
        public EditorText DisplayText => new(Key, TargetName);
        public long RetainedBytes => JsonSerializer.SerializeToUtf8Bytes(new[] { Before, After }).LongLength;
        public void Undo() => Apply(After, Before);
        public void Redo() => Apply(Before, After);
        private void Apply(BranchState expected, BranchState value)
        {
            var graph = Source.CurrentDocument;
            var node = graph.Nodes.SingleOrDefault(node => node.Id == NodeId) ?? throw BranchChanged();
            if (JsonSerializer.Serialize(BranchState.Capture(node, graph)) != JsonSerializer.Serialize(expected)) throw BranchChanged();
            // Keep live DTO identity for text editors and locked inspectors; records retain only author data.
            node.Options = value.Options?.Select(item =>
            {
                var live = node.Options?.SingleOrDefault(option => option.Id == item.Id) ?? new();
                live.Id = item.Id; live.Text = item.Text; live.Condition = item.Condition; return live;
            }).ToList();
            node.Conditions = value.Conditions?.Select(item =>
            {
                var live = node.Conditions?.SingleOrDefault(condition => condition.Id == item.Id) ?? new();
                live.Id = item.Id; live.Expression = item.Expression; return live;
            }).ToList();
            var old = graph.Edges.Where(edge => edge.FromNodeId == NodeId).ToDictionary(edge => edge.Id);
            graph.Edges.RemoveAll(edge => edge.FromNodeId == NodeId);
            foreach (var edge in value.Edges)
            {
                var live = old.GetValueOrDefault(edge.Id) ?? Copy(edge);
                live.FromOutlet = edge.FromOutlet;
                graph.Edges.Insert(Math.Min(value.EdgeIndices[value.Edges.IndexOf(edge)], graph.Edges.Count), live);
            }
            Changed(Source);
        }
    }
}
