using System.Text.Json.Serialization;

namespace GalNet.Core.Gallery;

/// <summary>A stable resource entry exposed through Gallery.</summary>
public sealed record GalleryItem
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("typeId")]
    public string TypeId { get; init; } = "";

    [JsonPropertyName("resourceId")]
    public string ResourceId { get; init; } = "";

    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; init; }

    [JsonPropertyName("sortOrder")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SortOrder { get; init; }
}
