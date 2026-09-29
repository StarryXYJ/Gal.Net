namespace GalNet.Core.Serialization;

/// <summary>
/// 打包清单 —— 描述导出包的结构、版本和资源索引。
/// </summary>
public sealed class Manifest
{
    /// <summary>格式版本号</summary>
    public int Version { get; set; } = 1;

    /// <summary>包唯一标识</summary>
    public string PackageId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>游戏名称（显示用）</summary>
    public string GameName { get; set; } = "";

    /// <summary>入口图 ID</summary>
    public string EntryGraphId { get; set; } = "";

    /// <summary>创建时间戳（ISO 8601）</summary>
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("O");

    /// <summary>资源索引列表</summary>
    public List<AssetIndexEntry> Assets { get; init; } = [];

    /// <summary>是否加密</summary>
    public bool Encrypted { get; set; }

    /// <summary>整体 SHA256 哈希值（十六进制字符串）</summary>
    public string Hash { get; set; } = "";
}

/// <summary>
/// 资源索引条目 —— 描述单个资源文件的元数据。
/// </summary>
public sealed class AssetIndexEntry
{
    /// <summary>资源唯一 ID</summary>
    public string Id { get; init; } = "";

    /// <summary>资源类型（"Image", "Audio", "Video", "Font" 等）</summary>
    public string Type { get; set; } = "";

    /// <summary>原始文件名</summary>
    public string FileName { get; set; } = "";

    /// <summary>包内相对路径</summary>
    public string BundlePath { get; set; } = "";

    /// <summary>文件大小（字节）</summary>
    public long SizeBytes { get; set; }

    /// <summary>SHA256 哈希值</summary>
    public string Hash { get; set; } = "";

    /// <summary>是否压缩</summary>
    public bool Compressed { get; set; }
}
