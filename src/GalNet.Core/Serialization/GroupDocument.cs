using System.Text.Json;
using System.Text.Json.Serialization;

namespace GalNet.Core.Serialization;

/// <summary>Serialized group document. Raw documents are editor source; compiled documents are Runtime input.</summary>
public sealed class GroupDocument
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("kind")]
    [JsonConverter(typeof(JsonStringEnumConverter<GroupDocumentKind>))]
    public GroupDocumentKind Kind { get; set; } = GroupDocumentKind.Raw;

    [JsonPropertyName("entries")]
    public List<GroupEntryDocument> Entries { get; set; } = [];
}

/// <summary>The stage represented by a serialized group document.</summary>
public enum GroupDocumentKind
{
    Raw,
    Compiled
}

/// <summary>Stable authoring entry. Parameters remain structured JSON until compiled for Runtime.</summary>
public sealed class GroupEntryDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("condition")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string Condition { get; set; } = "";

    [JsonPropertyName("parameters")]
    public Dictionary<string, JsonElement> Parameters { get; set; } = new(StringComparer.Ordinal);
}
