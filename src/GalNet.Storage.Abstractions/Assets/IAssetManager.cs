namespace GalNet.Core.Assets;

/// <summary>Host-provided resource lookup, conversion and cache service.</summary>
public interface IAssetManager : IDisposable
{
    Task<IGameFile?> GetFileAsync(string assetId, CancellationToken ct = default);
    Task<IReadOnlyList<IGameFile>> GetFilesAsync(ResourceType? type = null, CancellationToken ct = default);
    Task<T?> LoadAsync<T>(string assetId, CancellationToken ct = default) where T : class;
    /// <summary>Loads a file that has already been resolved from an archive, avoiding a second provider lookup.</summary>
    Task<T?> LoadAsync<T>(IGameFile file, CancellationToken ct = default) where T : class;
    Task<T?> LoadByPathAsync<T>(string path, CancellationToken ct = default) where T : class;
    void RegisterDecoder<T>(IAssetDecoder<T> decoder) where T : class;
    bool TryGetLoaded<T>(string assetId, out T asset) where T : class;
    void Release(string assetId);
    void Release<T>(string assetId) where T : class;
    bool IsLoaded(string assetId);
    int CachedCount { get; }
    /// <summary>Registers a provider owned by this manager; disposable providers are released on manager disposal.</summary>
    void RegisterProvider(IAssetProvider provider);
    void ClearCache();
}

/// <summary>Platform/domain conversion from immutable game-file bytes to one cached asset type.</summary>
public interface IAssetDecoder<T> where T : class
{
    ValueTask<T?> DecodeAsync(IGameFile file, ReadOnlyMemory<byte> data, CancellationToken ct = default);
}
