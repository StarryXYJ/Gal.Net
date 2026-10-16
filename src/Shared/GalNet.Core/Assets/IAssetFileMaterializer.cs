namespace GalNet.Core.Assets;

/// <summary>Provides caller-owned local files for backends that cannot consume resource bytes.</summary>
public interface IAssetFileMaterializer : IDisposable, IAsyncDisposable
{
    Task<AssetFileLease> AcquireAsync(string assetId, CancellationToken cancellationToken = default);
}

/// <summary>A materialized file remains valid until its last lease is released.</summary>
public sealed class AssetFileLease(string assetId, string typeId, string path, Action release) : IDisposable
{
    private Action? _release = release;
    public string AssetId { get; } = assetId;
    public string TypeId { get; } = typeId;
    public string Path { get; } = path;
    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}
