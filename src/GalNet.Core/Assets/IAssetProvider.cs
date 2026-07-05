namespace GalNet.Core.Assets;

/// <summary>
/// 资源归档提供者 —— 负责提供 IArchive 实例。
/// 开发模式：LocalFileProvider 从文件系统读取原始文件 + .meta
/// 打包模式：PakFileProvider 从 .pak 文件读取已打包数据
/// 可扩展：HttpProvider 从远程服务器加载
/// </summary>
public interface IAssetProvider
{
    /// <summary>提供者名称（用于日志和诊断）</summary>
    string Name { get; }

    /// <summary>异步打开指定名称的归档。</summary>
    Task<IArchive> OpenArchiveAsync(string archiveName, CancellationToken ct = default);

    /// <summary>同步打开指定名称的归档。</summary>
    IArchive OpenArchive(string archiveName);

    /// <summary>检查指定归档是否存在。</summary>
    bool Exists(string archiveName);
}
