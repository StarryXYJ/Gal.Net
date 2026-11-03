namespace GalNet.Core.Assets;

/// <summary>A collection of addressable game files, such as a pak archive.</summary>
public interface IArchive : IDisposable
{
    string Name { get; }
    bool Contains(string assetId);
    IEnumerable<string> AssetIds { get; }
    /// <summary>Observes cancellation before lookup and during any I/O or decoding.</summary>
    IGameFile? GetAsset(string assetId, CancellationToken ct = default);
    /// <summary>Observes cancellation before lookup and during any I/O or decoding.</summary>
    IGameFile? GetAssetByPath(string path, CancellationToken ct = default);
}
