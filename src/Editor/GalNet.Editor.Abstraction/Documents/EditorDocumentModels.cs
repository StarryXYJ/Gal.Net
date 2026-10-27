using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Core.Configuration;
using GalNet.Core.Entry;
using GalNet.Core.Parameters;
using GalNet.Core.Variable;

namespace GalNet.Editor.Abstraction.Documents;

public sealed class EditorGraphDocument
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 2;

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Main";

    [JsonPropertyName("rootNodeId")]
    public string RootNodeId { get; set; } = "";

    [JsonPropertyName("nodes")]
    public List<EditorGraphNodeDto> Nodes { get; set; } = [];

    [JsonPropertyName("edges")]
    public List<EditorGraphEdgeDto> Edges { get; set; } = [];

    [JsonPropertyName("playerVariables")]
    public List<ProjectVariableDefinition> PlayerVariables { get; set; } = [];

    [JsonPropertyName("saveVariables")]
    public List<ProjectVariableDefinition> SaveVariables { get; set; } = [];
}

public sealed class EditorGraphNodeDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("file")]
    public string? File { get; set; }

    [JsonPropertyName("branchType")]
    public string? BranchType { get; set; }

    [JsonPropertyName("options")]
    public List<EditorGraphBranchOptionDto>? Options { get; set; }

    [JsonPropertyName("conditions")]
    public List<EditorGraphBranchConditionDto>? Conditions { get; set; }
}

public sealed class EditorGraphBranchOptionDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("condition")]
    public string Condition { get; set; } = "";
}

public sealed class EditorGraphBranchConditionDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("expression")]
    public string Expression { get; set; } = "";
}

public sealed class EditorGraphEdgeDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("fromNodeId")]
    public string FromNodeId { get; set; } = "";

    [JsonPropertyName("fromOutlet")]
    public int FromOutlet { get; set; }

    [JsonPropertyName("toNodeId")]
    public string ToNodeId { get; set; } = "";
}

public sealed class EditorEntryData
{
    public string StableId { get; set; } = "";
    public int Id { get; set; }
    public string Type { get; set; } = "";
    public string Condition { get; set; } = "";
    /// <summary>Owned author JSON. A missing key and a provided JSON null are distinct.</summary>
    public Dictionary<string, JsonElement> Parameters { get; set; } = new();

    /// <summary>Text-to-JSON adaptation lives at the author input boundary, not in Core entries.</summary>
    public static Dictionary<string, JsonElement> CreateParameters(IEntryCatalog catalog, string type,
        IReadOnlyDictionary<string, string>? text = null)
    {
        var entry = catalog.Create(type, arguments: text is null ? null : JsonSerializer.SerializeToElement(ParseParameters(catalog, type, text)));
        return entry.Arguments.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
    }

    public static Dictionary<string, JsonElement> ParseParameters(IEntryCatalog catalog, string type, IReadOnlyDictionary<string, string> values) =>
        values.ToDictionary(pair => pair.Key,
            pair => catalog.TryGet(type, out var schema) && schema.DynamicParameters.ContainsKey(pair.Key)
                ? ValueCodec.ParseEditorText(schema.DynamicParameters[pair.Key].Value, pair.Value).Clone()
                : JsonSerializer.Deserialize<JsonElement>(pair.Value).Clone(), StringComparer.Ordinal);

    public Dictionary<string, string> GetParameterText(IEntryCatalog catalog) => Parameters.ToDictionary(pair => pair.Key,
        pair => catalog.TryGet(Type, out var schema) && schema.DynamicParameters.ContainsKey(pair.Key)
            ? ValueCodec.ToEditorText(schema.DynamicParameters[pair.Key].Value, pair.Value) : pair.Value.GetRawText(), StringComparer.Ordinal);
}

/// <summary>Project-owned content identity; its path can change without replacing the document.</summary>
public sealed class EditorGroupDocument(string groupId, string relativePath, List<EditorEntryData> entries)
{
    public string Id { get; } = groupId;
    public string DocumentId => "group/" + Id;
    public string RelativePath { get; private set; } = relativePath;
    public void ChangeSourcePath(string relativePath) => RelativePath = relativePath;
    public List<EditorEntryData> Entries { get; } = entries;
}

public sealed class LoadedEditorProjectDocument
{
    public EditorGraphDocument Document { get; set; } = new();
    public Dictionary<string, List<EditorEntryData>> GroupEntries { get; set; } = [];
}

/// <summary>
/// UI-independent aggregate edited by command handlers. Presentation models are projections of this object.
/// </summary>
public sealed class EditorProjectDocument
{
    public EditorGraphDocument Graph { get; set; } = new();
    public Dictionary<string, List<EditorEntryData>> GroupEntries { get; set; } = [];
    public ProjectConfiguration Configuration { get; set; } = new();
    public Dictionary<string, EditorFileDocument> Files { get; set; } = [];
}

/// <summary>Session identity survives path changes and closed views. Only managed text bytes participate in saving.</summary>
public sealed class EditorFileDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Path { get; set; } = "";
    public bool Exists { get; set; } = true;
    public bool ManagedContent { get; set; }
    public byte[] Data { get; set; } = [];
}
