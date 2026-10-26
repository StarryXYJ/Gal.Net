using System.Text.Json.Serialization;
namespace GalNet.Contracts.Builtins.Gallery;

public sealed record GalleryAnnotation
{
    [JsonExtensionData] public Dictionary<string, System.Text.Json.JsonElement>? ExtensionData { get; init; }
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("typeId")] public string TypeId { get; init; } = "";
    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Title { get; init; }
}
