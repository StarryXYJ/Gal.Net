namespace GalNet.Core.Assets;

/// <summary>Host-provided resource lookup, conversion and cache service.</summary>
public interface IAssetManager : IDisposable
{
    Task<IGameFile?> GetFileAsync(string assetId, CancellationToken ct = default);
    Task<IReadOnlyList<IGameFile>> GetFilesAsync(string? typeId = null, CancellationToken ct = default);
    Task<AssetHandle<T>?> AcquireAsync<T>(string assetId, CancellationToken ct = default) where T : class;
    Task<AssetHandle<T>?> AcquireByPathAsync<T>(string path, CancellationToken ct = default) where T : class;
    void RegisterDecoder<T>(string resourceTypeId, IAssetDecoder<T> decoder) where T : class;
    /// <summary>Registers a provider owned by this manager; disposable providers are released on manager disposal.</summary>
    void RegisterProvider(IAssetProvider provider);
}

/// <summary>Platform/domain conversion from immutable game-file bytes to one cached asset type.</summary>
public interface IAssetDecoder<T> where T : class
{
    ValueTask<T?> DecodeAsync(IGameFile file, ReadOnlyMemory<byte> data, CancellationToken ct = default);
}
