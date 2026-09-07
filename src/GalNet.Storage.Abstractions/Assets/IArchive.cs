namespace GalNet.Core.Assets;

/// <summary>A collection of addressable game files, such as a pak archive.</summary>
public interface IArchive : IDisposable
{
    string Name { get; }
    bool Contains(string assetId);
    IEnumerable<string> AssetIds { get; }
    IGameFile? GetAsset(string assetId);
    IGameFile? GetAssetByPath(string path);
}
