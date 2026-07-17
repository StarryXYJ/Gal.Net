using GalNet.Editor.Abstraction.Documents;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Media;

namespace GalNet.Editor.Abstraction.Changes;

public interface IEditorChange
{
    IReadOnlyList<string> ChangedResources { get; }
    EditorProjectDocument Apply();
    EditorProjectDocument Revert();
}

/// <summary>
/// Atomic immutable document transition. It is intentionally UI-independent and is safe to store in history.
/// Granular changes can replace this implementation without changing the session contract.
/// </summary>
public sealed class EditorDocumentChange : IEditorChange
{
    private readonly EditorProjectDocument _before;
    private readonly EditorProjectDocument _after;

    public IReadOnlyList<string> ChangedResources { get; }

    public EditorDocumentChange(
        EditorProjectDocument before,
        EditorProjectDocument after,
        IReadOnlyList<string> changedResources)
    {
        _before = EditorDocumentCloner.Clone(before);
        _after = EditorDocumentCloner.Clone(after);
        ChangedResources = changedResources;
    }

    public EditorProjectDocument Apply() => EditorDocumentCloner.Clone(_after);
    public EditorProjectDocument Revert() => EditorDocumentCloner.Clone(_before);
}

public static class EditorDocumentCloner
{
    public static EditorProjectDocument Clone(EditorProjectDocument source) => new()
    {
        Graph = CloneGraph(source.Graph),
        GroupEntries = source.GroupEntries.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Select(CloneEntry).ToList()),
        Settings = CloneSettings(source.Settings),
        UiProject = CloneUiProject(source.UiProject)
    };

    public static EditorGraphDocument CloneGraph(EditorGraphDocument source) => new()
    {
        Version = source.Version,
        Name = source.Name,
        RootNodeId = source.RootNodeId,
        Nodes = source.Nodes.Select(CloneNode).ToList(),
        Edges = source.Edges.Select(CloneEdge).ToList(),
        PlayerVariables = source.PlayerVariables.Select(variable => variable.Clone()).ToList(),
        SaveVariables = source.SaveVariables.Select(variable => variable.Clone()).ToList()
    };

    private static EditorGraphNodeDto CloneNode(EditorGraphNodeDto source) => new()
    {
        Id = source.Id,
        Type = source.Type,
        Name = source.Name,
        X = source.X,
        Y = source.Y,
        File = source.File,
        BranchType = source.BranchType,
        Options = source.Options?.Select(option => new EditorGraphBranchOptionDto
        {
            Id = option.Id,
            Text = option.Text,
            Condition = option.Condition
        }).ToList(),
        Conditions = source.Conditions?.Select(condition => new EditorGraphBranchConditionDto
        {
            Id = condition.Id,
            Expression = condition.Expression
        }).ToList()
    };

    private static EditorGraphEdgeDto CloneEdge(EditorGraphEdgeDto source) => new()
    {
        Id = source.Id,
        FromNodeId = source.FromNodeId,
        FromOutlet = source.FromOutlet,
        ToNodeId = source.ToNodeId
    };

    private static EditorEntryData CloneEntry(EditorEntryData source) => new()
    {
        StableId = source.StableId,
        Id = source.Id,
        Type = source.Type,
        Condition = source.Condition,
        Parameters = source.Parameters
    };

    private static GalNet.Core.Settings.ProjectSettings CloneSettings(GalNet.Core.Settings.ProjectSettings source) => new()
    {
        TargetLocale = new GalNet.Core.I18n.I18nLocale(source.TargetLocale.Code),
        AvailableLocales = source.AvailableLocales.Select(locale => new GalNet.Core.I18n.I18nLocale(locale.Code)).ToList(),
        SaveSlotCount = source.SaveSlotCount,
        SfxChannelCount = source.SfxChannelCount,
        DefaultWidth = source.DefaultWidth,
        DefaultHeight = source.DefaultHeight,
        PlayerVariables = source.PlayerVariables.Select(variable => variable.Clone()).ToList(),
        SaveVariables = source.SaveVariables.Select(variable => variable.Clone()).ToList()
    };

    public static GalNet.Core.UI.UiProject CloneUiProject(GalNet.Core.UI.UiProject source)
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new ProjectColorJsonConverter(), new JsonStringEnumConverter() }
        };
        return JsonSerializer.Deserialize<GalNet.Core.UI.UiProject>(JsonSerializer.Serialize(source, options), options)
            ?? new GalNet.Core.UI.UiProject();
    }

    private sealed class ProjectColorJsonConverter : JsonConverter<Color>
    {
        public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String && Color.TryParse(reader.GetString(), out var parsed))
                return parsed;
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException("A color must be an ARGB object.");
            byte a = 0, r = 0, g = 0, b = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var name = reader.GetString();
                reader.Read();
                var value = reader.GetByte();
                switch (name) { case "A": a = value; break; case "R": r = value; break; case "G": g = value; break; case "B": b = value; break; }
            }
            return Color.FromArgb(a, r, g, b);
        }

        public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("A", value.A); writer.WriteNumber("R", value.R);
            writer.WriteNumber("G", value.G); writer.WriteNumber("B", value.B);
            writer.WriteEndObject();
        }
    }
}
