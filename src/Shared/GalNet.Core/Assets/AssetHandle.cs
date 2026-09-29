namespace GalNet.Core.Assets;

/// <summary>One caller-owned reference to a decoded asset managed by <see cref="IAssetManager"/>.</summary>
public sealed class AssetHandle<T> : IDisposable where T : class
{
    private Action? _release;

    /// <summary>Creates a handle for one successful asset acquire. Only an asset manager should create handles.</summary>
    public AssetHandle(string assetId, string typeId, T value, Action release)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeId);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(release);
        AssetId = assetId;
        TypeId = typeId;
        Value = value;
        _release = release;
    }

    public string AssetId { get; }
    public string TypeId { get; }
    public T Value { get; }
    public bool IsReleased => _release is null;

    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}
