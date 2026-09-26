using GalNet.Core.Gallery;

namespace GalNet.Core.Assets;

/// <summary>Common, portable metadata written beside every asset.</summary>
public class AssetMeta
{
    /// <summary>全局唯一资源 ID（GUID）</summary>
    public string Id { get; set; } = "";

    /// <summary>Stable resource-type discriminator, serialized as <c>type</c>.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("type")]
    public string TypeId { get; set; } = "";

    /// <summary>相对于 Assets 目录的路径</summary>
    public string Path { get; set; } = "";

    /// <summary>压缩格式（none / deflate / gzip / brotli）</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Compress { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("gallery")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<GalleryAnnotation>? Gallery { get; set; }

    /// <summary>Fields not understood by the current DTO are retained when metadata is rewritten.</summary>
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? ExtensionData { get; set; }

    /// <summary>从字符串解析压缩模式</summary>
    public CompressionMode ParseCompression() => (Compress ?? "none").ToLowerInvariant() switch
    {
        "deflate" => CompressionMode.Deflate,
        "gzip" => CompressionMode.GZip,
        "brotli" => CompressionMode.Brotli,
        _ => CompressionMode.None,
    };
}

public sealed class SpriteAssetMeta : AssetMeta
{
    [System.Text.Json.Serialization.JsonPropertyName("filter")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Filter { get; set; }
}

public sealed class AudioAssetMeta : AssetMeta;
public sealed class VideoAssetMeta : AssetMeta;
public sealed class FontAssetMeta : AssetMeta;
public sealed class EffectProgramAssetMeta : AssetMeta;
public sealed class BinaryAssetMeta : AssetMeta;
