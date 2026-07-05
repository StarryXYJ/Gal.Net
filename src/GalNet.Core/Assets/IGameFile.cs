namespace GalNet.Core.Assets;

/// <summary>
/// 资源文件 —— 包含元数据和原始数据访问。
/// 对应一个资源文件及其 .meta 描述。
/// </summary>
public interface IGameFile
{
    /// <summary>全局唯一资源 ID（GUID）</summary>
    string Id { get; }

    /// <summary>资源原始路径（相对于 Assets 目录）</summary>
    string Path { get; }

    /// <summary>资源类型</summary>
    ResourceType Type { get; }

    /// <summary>原始文件大小（未压缩字节数）</summary>
    long Length { get; }

    /// <summary>SHA256 哈希（十六进制字符串，可为空）</summary>
    string? Hash { get; }

    /// <summary>以只读流方式打开资源数据。</summary>
    Stream OpenRead();

    /// <summary>同步读取全部字节。</summary>
    byte[] ReadAllBytes();

    /// <summary>异步读取全部字节。</summary>
    Task<byte[]> ReadAllBytesAsync(CancellationToken ct = default);
}
