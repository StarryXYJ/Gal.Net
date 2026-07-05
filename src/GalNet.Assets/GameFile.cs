using GalNet.Core.Assets;

namespace GalNet.Assets;

/// <summary>
/// 资源文件实现 —— 基于内存字节数组的 IGameFile。
/// 可来自原始文件、.pak 数据块等来源。
/// </summary>
public sealed class GameFile : IGameFile
{
    private readonly byte[] _data;

    public GameFile(string id, string path, ResourceType type, byte[] data, string? hash = null)
    {
        Id = id;
        Path = path;
        Type = type;
        _data = data;
        Hash = hash;
    }

    public string Id { get; }
    public string Path { get; }
    public ResourceType Type { get; }
    public long Length => _data.Length;
    public string? Hash { get; }

    public Stream OpenRead() => new MemoryStream(_data, writable: false);

    public byte[] ReadAllBytes() => _data;

    public Task<byte[]> ReadAllBytesAsync(CancellationToken ct = default) =>
        Task.FromResult(_data);
}
