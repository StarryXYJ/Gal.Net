using System.Text.Json.Serialization;

namespace GalNet.Core.Gallery;

/// <summary>Associates one Gallery type with the resource type it contains.</summary>
public sealed record GalleryTypeRegistration
{
    [JsonPropertyName("typeId")]
    public string TypeId { get; init; } = "";

    [JsonPropertyName("resourceType")]
    public string ResourceTypeName { get; init; } = "";
}
