namespace GalNet.Core.Assets;

/// <summary>
/// 资源归档 —— 一组可寻址文件的集合。
/// 对应一个 .pak 文件或一个开发模式资源目录。
/// </summary>
public interface IArchive : IDisposable
{
    /// <summary>归档名称</summary>
    string Name { get; }

    /// <summary>是否包含指定 ID 的资源</summary>
    bool Contains(string assetId);

    /// <summary>所有资源的 ID 列表</summary>
    IEnumerable<string> AssetIds { get; }

    /// <summary>按 ID 获取资源（不存在则返回 null）</summary>
    IGameFile? GetAsset(string assetId);

    /// <summary>按路径获取资源（不存在则返回 null）</summary>
    IGameFile? GetAssetByPath(string path);
}
