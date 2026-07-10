using System.Collections.Generic;
using System.Text.Json.Serialization;
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
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("condition")]
    public string Condition { get; set; } = "";
}

public sealed class EditorGraphBranchConditionDto
{
    [JsonPropertyName("expression")]
    public string Expression { get; set; } = "";
}

public sealed class EditorGraphEdgeDto
{
    [JsonPropertyName("fromNodeId")]
    public string FromNodeId { get; set; } = "";

    [JsonPropertyName("fromOutlet")]
    public int FromOutlet { get; set; }

    [JsonPropertyName("toNodeId")]
    public string ToNodeId { get; set; } = "";
}

public sealed class EditorEntryData
{
    public int Id { get; set; }
    public string Type { get; set; } = "";
    public string Condition { get; set; } = "";
    public string Parameters { get; set; } = "";
}

public sealed class LoadedEditorProjectDocument
{
    public EditorGraphDocument Document { get; set; } = new();
    public Dictionary<string, List<EditorEntryData>> GroupEntries { get; set; } = [];
}
