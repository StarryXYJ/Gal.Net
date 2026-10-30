namespace GalNet.Core.Assets;

/// <summary>A collection of addressable game files, such as a pak archive.</summary>
public interface IArchive : IDisposable
{
    string Name { get; }
    bool Contains(string assetId);
    IEnumerable<string> AssetIds { get; }
    IGameFile? GetAsset(string assetId);
    IGameFile? GetAssetByPath(string path);

    /// <summary>Checks cancellation before lookup; implementations doing I/O or decoding should also observe it during work.</summary>
    IGameFile? GetAsset(string assetId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return GetAsset(assetId);
    }

    /// <summary>Checks cancellation before lookup; implementations doing I/O or decoding should also observe it during work.</summary>
    IGameFile? GetAssetByPath(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return GetAssetByPath(path);
    }
}
