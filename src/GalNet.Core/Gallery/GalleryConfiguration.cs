using System.Text.Json.Serialization;

namespace GalNet.Core.Gallery;

/// <summary>Serializable Gallery content stored in <c>gallery.json</c>.</summary>
public sealed class GalleryConfiguration
{
    public const int CurrentVersion = 2;

    [JsonPropertyName("version")]
    public int Version { get; set; } = CurrentVersion;

    [JsonPropertyName("types")]
    public List<GalleryTypeRegistration> Types { get; set; } = [];

    [JsonPropertyName("items")]
    public List<GalleryItem> Items { get; set; } = [];
}
