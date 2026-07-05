using System.Buffers.Binary;
using System.Text;
using GalNet.Core.Assets;

namespace GalNet.Assets;

/// <summary>
/// .pak 归档实现 —— 可寻址资源文件集合。
///
/// 二进制格式：
///   [Magic   ] 4 bytes   "GPAK"
///   [Ver     ] 4 bytes   int32
///   [Count   ] 4 bytes   int32
///   ── Entry Table ──
///   [IdLen   ] 4 bytes   int32
///   [Id      ] IdLen     UTF-8
///   [PathLen ] 4 bytes   int32
///   [Path    ] PathLen   UTF-8
///   [Type    ] 4 bytes   int32
///   [Offset  ] 8 bytes   int64   数据段起始偏移
///   [OrigLen ] 8 bytes   int64   原始大小(未压缩)
///   [StoredLen] 8 bytes  int64   存储大小(压缩后)
///   [Compress] 4 bytes   int32   CompressionMode
///   [Hash    ] 32 bytes  SHA256  (十六进制解码后)
///   ── Data Section ──
///   [Block 1]  StoredLen bytes
///   [Block 2]  StoredLen bytes
///   ...
/// </summary>
public sealed class Archive : IArchive
{
    private const string Magic = "GPAK";
    private const int CurrentVersion = 1;

    private readonly string _name;
    private readonly Dictionary<string, EntryDescriptor> _entries = new();
    private readonly Dictionary<string, string> _pathToId = new();
    private readonly byte[] _dataSection;
    private bool _disposed;

    private Archive(string name, byte[] dataSection, Dictionary<string, EntryDescriptor> entries,
        Dictionary<string, string> pathToId)
    {
        _name = name;
        _dataSection = dataSection;
        _entries = entries;
        _pathToId = pathToId;
    }

    public string Name => _name;
    public IEnumerable<string> AssetIds => _entries.Keys;

    public bool Contains(string assetId) => _entries.ContainsKey(assetId);

    public IGameFile? GetAsset(string assetId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_entries.TryGetValue(assetId, out var desc))
            return null;

        var raw = new ReadOnlySpan<byte>(_dataSection, (int)desc.Offset, (int)desc.StoredLength).ToArray();
        var data = desc.Compression != CompressionMode.None
            ? CompressionHelper.Decompress(raw, desc.Compression)
            : raw;

        return new GameFile(assetId, desc.Path, desc.Type, data, desc.Hash);
    }

    public IGameFile? GetAssetByPath(string path)
    {
        if (_pathToId.TryGetValue(NormalizePath(path), out var id))
            return GetAsset(id);
        return null;
    }

    public void Dispose()
    {
        _disposed = true;
        _entries.Clear();
        _pathToId.Clear();
    }

    // ── 序列化/反序列化 ──

    /// <summary>将二进制数据反序列化为 Archive。</summary>
    public static Archive Deserialize(string name, byte[] pakData)
    {
        var span = new ReadOnlySpan<byte>(pakData);
        var offset = 0;

        // Magic
        if (Encoding.UTF8.GetString(span[offset..(offset + 4)]) != Magic)
            throw new InvalidDataException("Invalid .pak magic number");
        offset += 4;

        // Version
        var version = BinaryPrimitives.ReadInt32LittleEndian(span[offset..]);
        offset += 4;
        if (version > CurrentVersion)
            throw new InvalidDataException($"Unsupported .pak version: {version}");

        // Entry count
        var count = BinaryPrimitives.ReadInt32LittleEndian(span[offset..]);
        offset += 4;

        var entries = new Dictionary<string, EntryDescriptor>(count);
        var pathToId = new Dictionary<string, string>(count);

        for (var i = 0; i < count; i++)
        {
            var idLen = BinaryPrimitives.ReadInt32LittleEndian(span[offset..]);
            offset += 4;
            var id = Encoding.UTF8.GetString(span[offset..(offset + idLen)]);
            offset += idLen;

            var pathLen = BinaryPrimitives.ReadInt32LittleEndian(span[offset..]);
            offset += 4;
            var path = Encoding.UTF8.GetString(span[offset..(offset + pathLen)]);
            offset += pathLen;

            var type = (ResourceType)BinaryPrimitives.ReadInt32LittleEndian(span[offset..]);
            offset += 4;

            var dataOffset = BinaryPrimitives.ReadInt64LittleEndian(span[offset..]);
            offset += 8;
            var origLen = BinaryPrimitives.ReadInt64LittleEndian(span[offset..]);
            offset += 8;
            var storedLen = BinaryPrimitives.ReadInt64LittleEndian(span[offset..]);
            offset += 8;

            var compress = (CompressionMode)BinaryPrimitives.ReadInt32LittleEndian(span[offset..]);
            offset += 4;

            var hashBytes = span[offset..(offset + 64)]; // 64 hex chars = 32 bytes
            var hash = Encoding.UTF8.GetString(hashBytes).TrimEnd('\0');
            offset += 64;

            entries[id] = new EntryDescriptor
            {
                Path = path,
                Type = type,
                Offset = dataOffset,
                OriginalLength = origLen,
                StoredLength = storedLen,
                Compression = compress,
                Hash = hash,
            };

            pathToId[NormalizePath(path)] = id;
        }

        // Data section starts at offset
        var dataSection = pakData[offset..];

        return new Archive(name, dataSection, entries, pathToId);
    }

    /// <summary>将 Archive 序列化为二进制数据（.pak 格式）。</summary>
    public static byte[] Serialize(string name, IReadOnlyList<IGameFile> files,
        CompressionMode compression = CompressionMode.Brotli)
    {
        // Build entry descriptors (compress if needed)
        var entryList = new List<(IGameFile file, byte[] storedData, CompressionMode actualCompression)>();
        foreach (var file in files)
        {
            var data = file.ReadAllBytes();
            var useCompression = compression != CompressionMode.None && data.Length > 256;
            var stored = useCompression ? CompressionHelper.Compress(data, compression) : data;
            entryList.Add((file, stored, useCompression ? compression : CompressionMode.None));
        }

        // Calculate sizes
        var headerSize = 4 + 4 + 4; // magic + version + count
        foreach (var (file, storedData, _) in entryList)
        {
            headerSize += 4 + Encoding.UTF8.GetByteCount(file.Id); // id
            headerSize += 4 + Encoding.UTF8.GetByteCount(file.Path); // path
            headerSize += 4 + 8 + 8 + 8 + 4 + 64; // type + offset + origLen + storedLen + compress + hash
        }

        var pakSize = headerSize;
        foreach (var (_, storedData, _) in entryList)
            pakSize += storedData.Length;

        var pak = new byte[pakSize];
        var span = new Span<byte>(pak);
        var pos = 0;

        // Magic
        Encoding.UTF8.GetBytes(Magic, span[pos..(pos + 4)]);
        pos += 4;

        // Version
        BinaryPrimitives.WriteInt32LittleEndian(span[pos..], CurrentVersion);
        pos += 4;

        // Entry count
        BinaryPrimitives.WriteInt32LittleEndian(span[pos..], entryList.Count);
        pos += 4;

        // Entry table
        var dataRelativeOffset = 0L;
        foreach (var (file, storedData, actualCompression) in entryList)
        {
            var hash = string.IsNullOrEmpty(file.Hash)
                ? CryptoHelper.HashSHA256(file.ReadAllBytes())
                : file.Hash;

            // Id
            var idBytes = Encoding.UTF8.GetBytes(file.Id);
            BinaryPrimitives.WriteInt32LittleEndian(span[pos..], idBytes.Length);
            pos += 4;
            idBytes.CopyTo(span[pos..]);
            pos += idBytes.Length;

            // Path
            var pathBytes = Encoding.UTF8.GetBytes(file.Path);
            BinaryPrimitives.WriteInt32LittleEndian(span[pos..], pathBytes.Length);
            pos += 4;
            pathBytes.CopyTo(span[pos..]);
            pos += pathBytes.Length;

            // Type
            BinaryPrimitives.WriteInt32LittleEndian(span[pos..], (int)file.Type);
            pos += 4;

            // Offset (relative to data section start)
            BinaryPrimitives.WriteInt64LittleEndian(span[pos..], dataRelativeOffset);
            pos += 8;

            // Original length
            BinaryPrimitives.WriteInt64LittleEndian(span[pos..], file.Length);
            pos += 8;

            // Stored length
            BinaryPrimitives.WriteInt64LittleEndian(span[pos..], storedData.Length);
            pos += 8;

            // Compression
            BinaryPrimitives.WriteInt32LittleEndian(span[pos..], (int)actualCompression);
            pos += 4;

            // Hash (64 hex chars)
            var hashBytes = Encoding.UTF8.GetBytes(hash.PadRight(64, '\0')[..64]);
            hashBytes.CopyTo(span[pos..]);
            pos += 64;

            dataRelativeOffset += storedData.Length;
        }

        // Data section
        foreach (var (_, storedData, _) in entryList)
        {
            storedData.CopyTo(span[pos..]);
            pos += storedData.Length;
        }

        return pak;
    }

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/').TrimStart('/').ToLowerInvariant();

    private struct EntryDescriptor
    {
        public string Path;
        public ResourceType Type;
        public long Offset;
        public long OriginalLength;
        public long StoredLength;
        public CompressionMode Compression;
        public string Hash;
    }
}
