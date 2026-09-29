using System.Text.Json.Serialization;
namespace GalNet.Core.Gallery;
public sealed record GalleryAnnotation
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("typeId")] public string TypeId { get; init; } = "";
    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Title { get; init; }
}
