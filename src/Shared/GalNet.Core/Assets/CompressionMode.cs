namespace GalNet.Core.Assets;

/// <summary>
/// 压缩模式。
/// </summary>
public enum CompressionMode
{
    /// <summary>不压缩</summary>
    None = 0,
    /// <summary>Deflate 压缩</summary>
    Deflate,
    /// <summary>GZip 压缩</summary>
    GZip,
    /// <summary>Brotli 压缩（默认）</summary>
    Brotli,
}
