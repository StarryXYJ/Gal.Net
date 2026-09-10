using System.Text.Json;
using System.Text.Json.Serialization;

namespace GalNet.Core.Serialization;

/// <summary>Authoring document stored in a JSON .galgroup file.</summary>
public sealed class GroupDocument
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("entries")]
    public List<GroupEntryDocument> Entries { get; set; } = [];
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
