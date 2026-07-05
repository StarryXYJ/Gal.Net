namespace GalNet.Core.Assets;

/// <summary>
/// 资源管理器 —— 加载、缓存（引用计数）、释放资源。
/// 全局统一入口，内部委托给 IAssetProvider 加载实际数据。
/// </summary>
public interface IAssetManager : IDisposable
{
    /// <summary>
    /// 异步加载指定 ID 的资源。
    /// 若已缓存则直接返回，否则从 provider 加载并缓存。
    /// </summary>
    Task<T?> LoadAsync<T>(string assetId, CancellationToken ct = default) where T : class;

    /// <summary>
    /// 按资源路径异步加载（如 "bg/classroom.png"）。
    /// 内部转换为对应的 assetId 后走统一缓存，与 LoadAsync 共享缓存。
    /// </summary>
    Task<T?> LoadByPathAsync<T>(string path, CancellationToken ct = default) where T : class;

    /// <summary>
    /// 释放指定 ID 的资源（引用计数减一）。
    /// 引用归零时从缓存中移除并释放内存。
    /// </summary>
    void Release(string assetId);

    /// <summary>指定 ID 的资源是否已加载到缓存中。</summary>
    bool IsLoaded(string assetId);

    /// <summary>当前缓存的资源数量。</summary>
    int CachedCount { get; }

    /// <summary>注册一个归档提供者。</summary>
    void RegisterProvider(IAssetProvider provider);

    /// <summary>清除所有缓存（强制释放所有已缓存资源）。</summary>
    void ClearCache();
}
