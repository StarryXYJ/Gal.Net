namespace GalNet.Core.Assets;

/// <summary>Opens named resource archives from a host-defined storage backend.</summary>
public interface IAssetProvider
{
    string Name { get; }
    Task<IArchive> OpenArchiveAsync(string archiveName, CancellationToken ct = default);
    IArchive OpenArchive(string archiveName);
    bool Exists(string archiveName);
}
