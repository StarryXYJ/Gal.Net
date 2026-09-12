namespace GalNet.Core.Assets;

/// <summary>Host-provided resource lookup, conversion and cache service.</summary>
public interface IAssetManager : IDisposable
{
    Task<IGameFile?> GetFileAsync(string assetId, CancellationToken ct = default);
    Task<IReadOnlyList<IGameFile>> GetFilesAsync(ResourceType? type = null, CancellationToken ct = default);
    Task<T?> LoadAsync<T>(string assetId, CancellationToken ct = default) where T : class;
    Task<T?> LoadByPathAsync<T>(string path, CancellationToken ct = default) where T : class;
    void RegisterDecoder<T>(IAssetDecoder<T> decoder) where T : class;
    bool TryGetLoaded<T>(string assetId, out T asset) where T : class;
    void Release(string assetId);
    void Release<T>(string assetId) where T : class;
    bool IsLoaded(string assetId);
    int CachedCount { get; }
    void RegisterProvider(IAssetProvider provider);
    void ClearCache();
}

/// <summary>Platform/domain conversion from immutable game-file bytes to one cached asset type.</summary>
public interface IAssetDecoder<T> where T : class
{
    ValueTask<T?> DecodeAsync(IGameFile file, ReadOnlyMemory<byte> data, CancellationToken ct = default);
}
