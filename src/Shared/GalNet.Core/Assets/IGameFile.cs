namespace GalNet.Core.Assets;

/// <summary>Resource file metadata and raw data access.</summary>
public interface IGameFile
{
    string Id { get; }
    string Path { get; }
    string TypeId { get; }
    AssetMeta Metadata { get; }
    long Length { get; }
    string? Hash { get; }
    Stream OpenRead();
    byte[] ReadAllBytes();
    Task<byte[]> ReadAllBytesAsync(CancellationToken ct = default);
}
