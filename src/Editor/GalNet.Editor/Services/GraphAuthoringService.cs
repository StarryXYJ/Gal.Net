using System.Text.Json;
using GalNet.Editor.Abstraction.Changes;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;

namespace GalNet.Editor.Services;

/// <summary>Project data edits. Records retain author data and IDs, never a workspace or control.</summary>
public sealed class GraphAuthoringService(IEditorDocumentService documents, EditorHistories histories, IProjectContentSession content)
{
    public async Task<string> AddNodeAsync(GraphNodeKind kind, double x, double y, string? fromId = null, int outlet = 0)
    {
        if (kind == GraphNodeKind.Entry) throw new InvalidOperationException("The project already owns its Entry root.");
        if (!double.IsFinite(x) || !double.IsFinite(y)) throw new InvalidDataException("Node coordinates must be finite.");
        var id = Guid.NewGuid().ToString("N");
        var node = new EditorGraphNodeDto
        {
            Id = id,
            Name = kind == GraphNodeKind.LinearGroup ? "New Group" : "New Branch",
            X = x,
            Y = y,
            Type = kind == GraphNodeKind.LinearGroup ? "Group" : "Branch",
            File = kind == GraphNodeKind.LinearGroup ? $"groups/{id}.rawgalgroup" : null,
            BranchType = kind == GraphNodeKind.ChoiceBranch ? "Choice" : kind == GraphNodeKind.ConditionBranch ? "Condition" : null,
            Options = kind == GraphNodeKind.ChoiceBranch ? [new() { Id = Guid.NewGuid().ToString("N"), Text = "Option 1" }] : null,
            Conditions = kind == GraphNodeKind.ConditionBranch ? [new() { Id = Guid.NewGuid().ToString("N"), Expression = "true" }] : null
        };
        // Creation with a connection is one structural intent, including the replaced output edge.
        if (fromId is not null) ValidateConnection(fromId, outlet, node);
        await histories.Content.ExecuteAsync(new PresenceEdit(documents, content, [id], new Bundle([(documents.CurrentDocument.Nodes.Count, node)], [],
            node.Type == "Group" ? new() { [id] = [] } : new()), fromId, outlet));
        return id;
    }

    public Task DeleteNodesAsync(IEnumerable<string> nodeIds)
    {
        var ids = nodeIds.Distinct(StringComparer.Ordinal).ToArray();
        foreach (var id in ids)
            if (Find(id).Type == "Entry") throw new InvalidOperationException("The Entry root cannot be deleted.");
        return ids.Length == 0 ? Task.CompletedTask : histories.Content.ExecuteAsync(new PresenceEdit(documents, content, ids));
    }

    public void Rename(string id, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException("Node name is required.");
        var before = Find(id).Name;
        if (before != value) histories.Content.Execute(new NameEdit(documents, id, before, value));
    }

    public void Move(IReadOnlyDictionary<string, (double X, double Y)> positions)
    {
        var changes = positions.Select(pair =>
        {
            var node = Find(pair.Key);
            if (!double.IsFinite(pair.Value.X) || !double.IsFinite(pair.Value.Y)) throw new InvalidDataException("Node coordinates must be finite.");
            return new Position(pair.Key, node.X, node.Y, pair.Value.X, pair.Value.Y);
        }).Where(item => item.X != item.NextX || item.Y != item.NextY).ToArray();
        if (changes.Length > 0) histories.Content.Execute(new MoveEdit(documents, changes));
    }

    public void Connect(string fromId, int outlet, string toId)
    {
        ValidateConnection(fromId, outlet, Find(toId));
        var before = documents.CurrentDocument.Edges.Select((edge, index) => (index, edge))
            .Where(pair => pair.edge.FromNodeId == fromId && pair.edge.FromOutlet == outlet).ToArray();
        if (before.Length == 1 && before[0].edge.ToNodeId == toId) return;
        var edge = new EditorGraphEdgeDto { Id = Guid.NewGuid().ToString("N"), FromNodeId = fromId, FromOutlet = outlet, ToNodeId = toId };
        histories.Content.Execute(new EdgeEdit(documents, before, [(documents.CurrentDocument.Edges.Count, edge)]));
    }

    public void DeleteEdge(string id)
    {
        var index = documents.CurrentDocument.Edges.FindIndex(edge => edge.Id == id);
        if (index >= 0) histories.Content.Execute(new EdgeEdit(documents, [(index, documents.CurrentDocument.Edges[index])], []));
    }

    private EditorGraphNodeDto Find(string id) => documents.CurrentDocument.Nodes.Single(node => node.Id == id);
    private void ValidateConnection(string fromId, int outlet, EditorGraphNodeDto to)
    {
        var from = Find(fromId);
        var count = from.BranchType == "Choice" ? Math.Max(1, from.Options?.Count ?? 0)
            : from.BranchType == "Condition" ? Math.Max(1, from.Conditions?.Count ?? 0) : 1;
        if (from.Id == to.Id || to.Type == "Entry" || outlet < 0 || outlet >= count)
            throw new InvalidDataException("Invalid graph connection.");
    }

    private static void Changed(IEditorDocumentService source) => source.SynchronizeGroups();
    private sealed record NameEdit(IEditorDocumentService Source, string Id, string Before, string After) : IUndoableEdit
    {
        public string Description => "Rename node";
        public long RetainedBytes => (Before.Length + After.Length + Id.Length) * 2L;
        public void Undo() => Apply(After, Before);
        public void Redo() => Apply(Before, After);
        private void Apply(string expected, string value)
        {
            var node = Source.CurrentDocument.Nodes.Single(node => node.Id == Id);
            if (node.Name != expected) throw new InvalidOperationException("The node name changed outside this edit.");
            node.Name = value; Changed(Source);
        }
    }
    private sealed record Position(string Id, double X, double Y, double NextX, double NextY);
    private sealed record MoveEdit(IEditorDocumentService Source, Position[] Positions) : IUndoableEdit
    {
        public string Description => "Move nodes";
        public long RetainedBytes => Positions.Sum(item => item.Id.Length * 2L + 32);
        public void Undo() => Apply(false);
        public void Redo() => Apply(true);
        private void Apply(bool forward)
        {
            var nodes = Positions.Select(item => (item, node: Source.CurrentDocument.Nodes.Single(node => node.Id == item.Id))).ToArray();
            foreach (var (item, node) in nodes)
                if (node.X != (forward ? item.X : item.NextX) || node.Y != (forward ? item.Y : item.NextY))
                    throw new InvalidOperationException("Node positions changed outside this edit.");
            foreach (var (item, node) in nodes) { node.X = forward ? item.NextX : item.X; node.Y = forward ? item.NextY : item.Y; }
            Changed(Source);
        }
    }
    private sealed record EdgeEdit(IEditorDocumentService Source, (int Index, EditorGraphEdgeDto Edge)[] Before,
        (int Index, EditorGraphEdgeDto Edge)[] After) : IUndoableEdit
    {
        public string Description => "Change connections";
        public long RetainedBytes => JsonSerializer.SerializeToUtf8Bytes(Before.Select(item => item.Edge).Concat(After.Select(item => item.Edge))).LongLength;
        private static bool Same(EditorGraphEdgeDto a, EditorGraphEdgeDto b) => a.Id == b.Id && a.FromNodeId == b.FromNodeId && a.ToNodeId == b.ToNodeId && a.FromOutlet == b.FromOutlet;
        public void Undo() => Apply(After, Before);
        public void Redo() => Apply(Before, After);
        private void Apply((int Index, EditorGraphEdgeDto Edge)[] remove, (int Index, EditorGraphEdgeDto Edge)[] add)
        {
            var edges = Source.CurrentDocument.Edges;
            if (remove.Any(item => !edges.Any(edge => Same(edge, item.Edge))) || add.Any(item => edges.Any(edge => edge.Id == item.Edge.Id)))
                throw new InvalidOperationException("Connections changed outside this edit.");
            foreach (var item in remove) edges.RemoveAll(edge => edge.Id == item.Edge.Id);
            foreach (var item in add.OrderBy(item => item.Index)) edges.Insert(Math.Min(item.Index, edges.Count), item.Edge);
            Changed(Source);
        }
    }

    private sealed record Bundle((int Index, EditorGraphNodeDto Node)[] Nodes, (int Index, EditorGraphEdgeDto Edge)[] Edges,
        Dictionary<string, List<EditorEntryData>> Groups)
    {
        public static Bundle Capture(EditorProjectDocument document, string[] ids) => new(
            document.Graph.Nodes.Select((node, index) => (index, node)).Where(item => ids.Contains(item.node.Id)).ToArray(),
            document.Graph.Edges.Select((edge, index) => (index, edge)).Where(item => ids.Contains(item.edge.FromNodeId) || ids.Contains(item.edge.ToNodeId)).ToArray(),
            document.GroupEntries.Where(pair => ids.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value));
        public long Bytes => JsonSerializer.SerializeToUtf8Bytes(new { Nodes = Nodes.Select(item => item.Node), Edges = Edges.Select(item => item.Edge), Groups }).LongLength;
    }

    private sealed class PresenceEdit(IEditorDocumentService source, IProjectContentSession session, string[] ids,
        Bundle? created = null, string? fromId = null, int outlet = 0) : IAsyncUndoableEdit
    {
        private readonly bool _isCreation = created is not null;
        private readonly bool _writesFiles = created?.Nodes.Any(item => item.Node.Type == "Group") == true || source.CurrentDocument.Nodes.Any(node => ids.Contains(node.Id) && node.Type == "Group");
        private Bundle? _working = created;
        private Bundle? _saved = created;
        private (int Index, EditorGraphEdgeDto Edge)[] _replacedWorking = [];
        private (int Index, EditorGraphEdgeDto Edge)[] _replacedSaved = [];
        public string Description => !_isCreation ? "Delete nodes and Group sources" : "Create node and source";
        public long RetainedBytes => (_working?.Bytes ?? 0) + (_saved?.Bytes ?? 0);
        public ValueTask RedoAsync() => SetAsync(_isCreation);
        public ValueTask UndoAsync() => SetAsync(!_isCreation);
        private async ValueTask SetAsync(bool present)
        {
            Bundle nextWorking = null!, nextSaved = null!;
            (int Index, EditorGraphEdgeDto Edge)[] replacedWorking = [], replacedSaved = [];
            IProjectStructureChange Prepare(EditorProjectDocument working, EditorProjectDocument saved)
            {
                if (!present && ids.Any(id => !working.Graph.Nodes.Any(node => node.Id == id)))
                    throw new InvalidOperationException("A queued node deletion no longer has its original targets.");
                if (present && fromId is not null)
                {
                    var from = working.Graph.Nodes.SingleOrDefault(node => node.Id == fromId)
                        ?? throw new InvalidOperationException("The starting node was removed before this edit.");
                    var count = from.BranchType == "Choice" ? Math.Max(1, from.Options?.Count ?? 0)
                        : from.BranchType == "Condition" ? Math.Max(1, from.Conditions?.Count ?? 0) : 1;
                    if (outlet < 0 || outlet >= count) throw new InvalidOperationException("The starting outlet changed before this edit.");
                }
                // Capture only after publication is available: an earlier save may advance Saved while we wait.
                var liveWorking = new EditorProjectDocument { Graph = source.CurrentDocument, GroupEntries = source.LoadedProject.GroupEntries };
                nextWorking = present ? _working! : Bundle.Capture(liveWorking, ids);
                nextSaved = present ? _saved! : Bundle.Capture(saved, ids);
                replacedWorking = present && fromId is not null ? Outgoing(working) : _replacedWorking;
                replacedSaved = present && fromId is not null ? Outgoing(saved) : _replacedSaved;
                return new PresenceChange(source.CurrentDocument, ids, nextWorking, nextSaved, present, fromId, outlet, replacedWorking, replacedSaved);
            }
            if (_writesFiles) await session.PublishStructureAsync(Prepare);
            else
            {
                var working = new EditorProjectDocument { Graph = source.CurrentDocument, GroupEntries = source.LoadedProject.GroupEntries };
                var change = Prepare(working, session.CaptureSaved());
                change.Apply(EditorDocumentCloner.Clone(working), AuthorDocumentVersion.Working);
                change.Apply(working, AuthorDocumentVersion.Working);
                Changed(source);
            }
            if (!present) { _working = nextWorking; _saved = nextSaved; }
            else { _replacedWorking = replacedWorking; _replacedSaved = replacedSaved; }
        }
        private (int Index, EditorGraphEdgeDto Edge)[] Outgoing(EditorProjectDocument document) =>
            document.Graph.Edges.Select((edge, index) => (index, edge)).Where(item => item.edge.FromNodeId == fromId && item.edge.FromOutlet == outlet).ToArray();
    }

    private sealed record PresenceChange(EditorGraphDocument LiveGraph, string[] Ids, Bundle Working, Bundle Saved, bool Present, string? FromId, int Outlet,
        (int Index, EditorGraphEdgeDto Edge)[] ReplacedWorking, (int Index, EditorGraphEdgeDto Edge)[] ReplacedSaved) : IProjectStructureChange
    {
        public void Apply(EditorProjectDocument document, AuthorDocumentVersion version)
        {
            var bundle = version == AuthorDocumentVersion.Working ? Working : Saved;
            var live = version == AuthorDocumentVersion.Working && ReferenceEquals(document.Graph, LiveGraph);
            var replaced = version == AuthorDocumentVersion.Working ? ReplacedWorking : ReplacedSaved;
            if (!Present)
            {
                document.Graph.Edges.RemoveAll(edge => Ids.Contains(edge.FromNodeId) || Ids.Contains(edge.ToNodeId));
                document.Graph.Nodes.RemoveAll(node => Ids.Contains(node.Id));
                foreach (var id in Ids) document.GroupEntries.Remove(id);
                foreach (var item in replaced.OrderBy(item => item.Index))
                    if (document.Graph.Nodes.Any(node => node.Id == item.Edge.FromNodeId) && document.Graph.Nodes.Any(node => node.Id == item.Edge.ToNodeId))
                        document.Graph.Edges.Insert(Math.Min(item.Index, document.Graph.Edges.Count), live ? item.Edge : Copy(item.Edge));
                return;
            }
            if (bundle.Nodes.Any(item => document.Graph.Nodes.Any(node => node.Id == item.Node.Id))) throw new InvalidOperationException("A node already exists.");
            foreach (var item in bundle.Nodes.OrderBy(item => item.Index)) document.Graph.Nodes.Insert(Math.Min(item.Index, document.Graph.Nodes.Count), live ? item.Node : Copy(item.Node));
            foreach (var pair in bundle.Groups) document.GroupEntries.Add(pair.Key, live ? pair.Value : Copy(pair.Value));
            foreach (var item in bundle.Edges.OrderBy(item => item.Index)) document.Graph.Edges.Insert(Math.Min(item.Index, document.Graph.Edges.Count), live ? item.Edge : Copy(item.Edge));
            if (FromId is not null && document.Graph.Nodes.Any(node => node.Id == FromId))
            {
                document.Graph.Edges.RemoveAll(edge => edge.FromNodeId == FromId && edge.FromOutlet == Outlet);
                document.Graph.Edges.Add(new() { Id = "edge-" + Ids[0], FromNodeId = FromId, FromOutlet = Outlet, ToNodeId = Ids[0] });
            }
        }
        private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value))!;
    }
}
