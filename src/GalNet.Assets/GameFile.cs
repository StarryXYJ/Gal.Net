using GalNet.Core.Assets;

namespace GalNet.Assets;

/// <summary>
/// 资源文件实现 —— 基于内存字节数组的 IGameFile。
/// 可来自原始文件、.pak 数据块等来源。
/// </summary>
public sealed class GameFile : IGameFile
{
    private readonly byte[] _data;

    public GameFile(string id, string path, string typeId, AssetMeta metadata, byte[] data, string? hash = null)
    {
        Id = id;
        Path = path;
        TypeId = typeId.Trim().ToLowerInvariant();
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _data = data;
        Hash = hash;
    }

    public string Id { get; }
    public string Path { get; }
    public string TypeId { get; }
    public AssetMeta Metadata { get; }
    public long Length => _data.Length;
    public string? Hash { get; }

    public Stream OpenRead() => new MemoryStream(_data, writable: false);

    public byte[] ReadAllBytes() => _data;

    public Task<byte[]> ReadAllBytesAsync(CancellationToken ct = default) =>
        Task.FromResult(_data);

}
