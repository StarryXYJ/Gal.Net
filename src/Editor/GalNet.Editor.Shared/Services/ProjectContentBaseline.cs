using System.Text.Json;
using GalNet.Core.Variable;
using GalNet.Editor.Abstraction.Documents;

namespace GalNet.Editor.Shared.Services;

/// <summary>Owns a saved snapshot. Live comparisons never clone or serialize the project.</summary>
internal sealed class ProjectContentBaseline
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private EditorProjectDocument _saved = new();
    public byte[] Bytes { get; private set; } = [];

    public void Set(byte[] bytes)
    {
        _saved = JsonSerializer.Deserialize<EditorProjectDocument>(bytes, Json)!;
        Bytes = bytes;
    }

    public bool Matches(EditorProjectDocument current) => Same(current, _saved);

    private static bool Same(EditorProjectDocument a, EditorProjectDocument b) =>
        a.Graph.Version == b.Graph.Version && a.Graph.Name == b.Graph.Name && a.Graph.RootNodeId == b.Graph.RootNodeId &&
        Sequence(a.Graph.Nodes, b.Graph.Nodes, Node) && Sequence(a.Graph.Edges, b.Graph.Edges, Edge) &&
        Sequence(a.Graph.PlayerVariables, b.Graph.PlayerVariables, Variable) && Sequence(a.Graph.SaveVariables, b.Graph.SaveVariables, Variable) &&
        Map(a.Configuration.Sections, b.Configuration.Sections, (x, y) => x.Version == y.Version && JsonElement.DeepEquals(x.Data, y.Data)) &&
        Map(a.GroupEntries, b.GroupEntries, (x, y) => Sequence(x, y, Entry)) && Map(a.Files, b.Files, File);

    private static bool Node(EditorGraphNodeDto a, EditorGraphNodeDto b) =>
        a.Id == b.Id && a.Type == b.Type && a.Name == b.Name && a.X.Equals(b.X) && a.Y.Equals(b.Y) && a.File == b.File && a.BranchType == b.BranchType && a.StoryPackage == b.StoryPackage &&
        Sequence(a.Options, b.Options, (x, y) => x.Id == y.Id && x.Text == y.Text && x.Condition == y.Condition) &&
        Sequence(a.Conditions, b.Conditions, (x, y) => x.Id == y.Id && x.Expression == y.Expression);
    private static bool Edge(EditorGraphEdgeDto a, EditorGraphEdgeDto b) =>
        a.Id == b.Id && a.FromNodeId == b.FromNodeId && a.FromOutlet == b.FromOutlet && a.ToNodeId == b.ToNodeId;
    private static bool Entry(EditorEntryData a, EditorEntryData b) =>
        a.StableId == b.StableId && a.Type == b.Type && a.Condition == b.Condition && Map(a.Parameters, b.Parameters, JsonElement.DeepEquals);
    private static bool Variable(ProjectVariableDefinition a, ProjectVariableDefinition b) =>
        a.Name == b.Name && a.Description == b.Description && a.DefaultValue.Uid == b.DefaultValue.Uid &&
        a.DefaultValue.Name == b.DefaultValue.Name && a.DefaultValue.Type == b.DefaultValue.Type &&
        (a.DefaultValue.Type switch
        {
            VariableType.Bool => a.DefaultValue.AsBool() == b.DefaultValue.AsBool(),
            VariableType.Int => a.DefaultValue.AsInt() == b.DefaultValue.AsInt(),
            VariableType.Float => a.DefaultValue.AsFloat().Equals(b.DefaultValue.AsFloat()),
            _ => a.DefaultValue.AsString() == b.DefaultValue.AsString()
        });
    private static bool File(EditorFileDocument a, EditorFileDocument b) =>
        a.Id == b.Id && a.Path == b.Path && a.Exists == b.Exists && a.ManagedContent == b.ManagedContent &&
        (!a.Exists || a.Data.AsSpan().SequenceEqual(b.Data));

    private static bool Sequence<T>(IReadOnlyList<T>? a, IReadOnlyList<T>? b, Func<T, T, bool> same)
    {
        if (a is null || b is null) return a is null && b is null;
        if (a.Count != b.Count) return false;
        for (var index = 0; index < a.Count; index++) if (!same(a[index], b[index])) return false;
        return true;
    }
    private static bool Map<T>(IReadOnlyDictionary<string, T> a, IReadOnlyDictionary<string, T> b, Func<T, T, bool> same)
    {
        if (a.Count != b.Count) return false;
        foreach (var (id, value) in a) if (!b.TryGetValue(id, out var other) || !same(value, other)) return false;
        return true;
    }
}
