namespace GalNet.Core.Assets;

/// <summary>Host-provided resource lookup, conversion and cache service.</summary>
public interface IAssetManager : IDisposable
{
    Task<IGameFile?> GetFileAsync(string assetId, CancellationToken ct = default);
    Task<IReadOnlyList<IGameFile>> GetFilesAsync(ResourceType? type = null, CancellationToken ct = default);
    Task<T?> LoadAsync<T>(string assetId, CancellationToken ct = default) where T : class;
    Task<T?> LoadByPathAsync<T>(string path, CancellationToken ct = default) where T : class;
    void Release(string assetId);
    bool IsLoaded(string assetId);
    int CachedCount { get; }
    void RegisterProvider(IAssetProvider provider);
    void ClearCache();
}
