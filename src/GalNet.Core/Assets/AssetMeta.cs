namespace GalNet.Core.Assets;

/// <summary>
/// 资源描述元数据 —— 对应 .meta JSON 文件。
/// </summary>
public sealed class AssetMeta
{
    /// <summary>全局唯一资源 ID（GUID）</summary>
    public string Id { get; set; } = "";

    /// <summary>资源类型</summary>
    public string Type { get; set; } = "unknown";

    /// <summary>相对于 Assets 目录的路径</summary>
    public string Path { get; set; } = "";

    /// <summary>滤波模式（point / bilinear / trilinear）</summary>
    public string Filter { get; set; } = "bilinear";

    /// <summary>压缩格式（none / deflate / gzip / brotli）</summary>
    public string Compress { get; set; } = "none";

    /// <summary>从字符串解析资源类型</summary>
    public ResourceType ParseResourceType() => Type.ToLowerInvariant() switch
    {
        "sprite" => ResourceType.Sprite,
        "audio" => ResourceType.Audio,
        "video" => ResourceType.Video,
        "font" => ResourceType.Font,
        _ => ResourceType.Unknown,
    };

    /// <summary>从字符串解析压缩模式</summary>
    public CompressionMode ParseCompression() => Compress.ToLowerInvariant() switch
    {
        "deflate" => CompressionMode.Deflate,
        "gzip" => CompressionMode.GZip,
        "brotli" => CompressionMode.Brotli,
        _ => CompressionMode.None,
    };
}
