using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Assets.Variants;
using GalNet.Core.Assets;
using GalNet.Core.Serialization;

namespace GalNet.Assets;

/// <summary>GPAK v3: magic/version, length-prefixed control JSON, entry count/table and data blocks.</summary>
public sealed class Archive : IPakArchive
{
    public const int CurrentVersion = 3;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions ControlOptions = JsonMetadata.CreateOptions(new(JsonSerializerDefaults.Web)
    {
        AllowDuplicateProperties = false,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    });
    private readonly Dictionary<string, EntryDescriptor> _entries;
    private readonly Dictionary<string, string> _pathToId;
    private readonly byte[] _dataSection;
    private readonly PakDescriptor _descriptor;
    private bool _disposed;

    private Archive(string name, byte[] dataSection, Dictionary<string, EntryDescriptor> entries,
        Dictionary<string, string> pathToId, PakDescriptor descriptor)
    {
        Name = name;
        _dataSection = dataSection;
        _entries = entries;
        _pathToId = pathToId;
        _descriptor = descriptor;
    }

    public string Name { get; }
    public PakDescriptor Descriptor => _descriptor with { Mappings = new(_descriptor.Mappings, StringComparer.OrdinalIgnoreCase) };
    public IEnumerable<string> AssetIds => _entries.Keys;
    public IReadOnlyList<PakAssetIndexEntry> MetadataIndex => _entries.Values.Select(entry => new PakAssetIndexEntry(
        entry.Metadata.Id, entry.Metadata.Path, entry.Metadata.TypeId, entry.Metadata, entry.OriginalLength, entry.Hash)).ToArray();
    public bool Contains(string assetId) => _entries.ContainsKey(assetId.Trim());

    public IGameFile? GetAsset(string assetId) => GetAsset(assetId, CancellationToken.None);

    public IGameFile? GetAsset(string assetId, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ct.ThrowIfCancellationRequested();
        if (!_entries.TryGetValue(assetId.Trim(), out var entry)) return null;
        using var source = new MemoryStream(_dataSection, (int)entry.Offset, (int)entry.StoredLength, writable: false);
        var data = CompressionHelper.Decompress(source, entry.Compression, (int)entry.OriginalLength, ct);
        if (data.LongLength != entry.OriginalLength || CryptoHelper.HashSHA256(data) != entry.Hash)
            throw new InvalidDataException($"PAK resource '{assetId}' failed size/hash verification.");
        ct.ThrowIfCancellationRequested();
        var file = new GameFile(entry.Metadata.Id, entry.Metadata.Path, entry.Metadata.TypeId, entry.Metadata, data, entry.Hash);
        ValidateCompiledDependencies(file);
        return file;
    }

    public IGameFile? GetAssetByPath(string path) => GetAssetByPath(path, CancellationToken.None);

    public IGameFile? GetAssetByPath(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return _pathToId.TryGetValue(AssetPathHelper.Normalize(path), out var id) ? GetAsset(id, ct) : null;
    }

    public void Dispose()
    {
        _disposed = true;
        _entries.Clear();
        _pathToId.Clear();
    }

    public static Archive Deserialize(string name, byte[] pakData, IResourceTypeCatalog resourceTypes) =>
        Deserialize(name, pakData, resourceTypes, CancellationToken.None);

    public static Archive Deserialize(string name, byte[] pakData, IResourceTypeCatalog resourceTypes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            using var stream = new MemoryStream(pakData, writable: false);
            using var reader = new BinaryReader(stream, Utf8);
            if (!ReadExact(reader, 4).AsSpan().SequenceEqual("GPAK"u8)) throw new InvalidDataException("Invalid .pak magic number.");
            var version = reader.ReadInt32();
            if (version != CurrentVersion) throw new InvalidDataException($"Unsupported .pak version: {version}.");
            var descriptor = JsonCodec.Deserialize<PakDescriptor>(ReadString(reader), ControlOptions)
                ?? throw new InvalidDataException("Missing PAK control descriptor.");
            descriptor.Validate();
            descriptor = descriptor with { Mappings = new(descriptor.Mappings, StringComparer.OrdinalIgnoreCase) };
            var count = reader.ReadInt32();
            if (count < 0 || count > (stream.Length - stream.Position) / 108)
                throw new InvalidDataException("Invalid PAK entry count.");
            var codec = new AssetMetaCodec(resourceTypes);
            var entries = new Dictionary<string, EntryDescriptor>(StringComparer.OrdinalIgnoreCase);
            var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var id = ReadString(reader);
                var path = ReadString(reader);
                var type = ReadString(reader);
                var metadata = codec.Deserialize(ReadString(reader));
                if (id != metadata.Id || path != metadata.Path || type != metadata.TypeId)
                    throw new InvalidDataException("PAK entry identity does not match its typed metadata.");
                var entry = new EntryDescriptor(metadata, reader.ReadInt64(), reader.ReadInt64(), reader.ReadInt64(),
                    (CompressionMode)reader.ReadInt32(), Utf8.GetString(ReadExact(reader, 64)));
                if (!Enum.IsDefined(entry.Compression) || entry.Offset < 0 || entry.OriginalLength < 0
                    || entry.OriginalLength > int.MaxValue || entry.StoredLength < 0 || entry.StoredLength > int.MaxValue
                    || entry.Hash.Length != 64 || entry.Hash.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
                    throw new InvalidDataException("Invalid PAK entry lengths, compression or SHA-256.");
                if (!entries.TryAdd(id, entry) || !paths.TryAdd(AssetPathHelper.Normalize(path), id))
                    throw new InvalidDataException("Duplicate PAK resource ID or logical path.");
            }
            var data = pakData[(int)stream.Position..];
            long position = 0;
            foreach (var entry in entries.Values)
            {
                if (entry.Offset != position || entry.StoredLength > data.LongLength - position)
                    throw new InvalidDataException("Invalid PAK data block range.");
                position += entry.StoredLength;
            }
            if (position != data.LongLength) throw new InvalidDataException("Unexpected PAK trailing bytes.");
            ValidateTargets(descriptor, entries.Keys);
            var archive = new Archive(name, data, entries, paths, descriptor);
            foreach (var id in entries.Where(pair => pair.Value.Metadata.TypeId == ImageVariantsCodec.ResourceType).Select(pair => pair.Key))
                archive.GetAsset(id, ct);
            return archive;
        }
        catch (Exception exception) when (exception is EndOfStreamException or JsonException or DecoderFallbackException or ArgumentException or OverflowException)
        {
            throw new InvalidDataException("Malformed PAK archive.", exception);
        }
    }

    public static byte[] Serialize(string name, IReadOnlyList<IGameFile> files,
        IResourceTypeCatalog resourceTypes, CompressionMode compression = CompressionMode.Brotli, PakDescriptor? descriptor = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (!Enum.IsDefined(compression)) throw new ArgumentOutOfRangeException(nameof(compression));
        descriptor ??= new();
        descriptor.Validate();
        descriptor = descriptor with
        {
            Mappings = descriptor.Mappings.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase)
        };
        var codec = new AssetMetaCodec(resourceTypes);
        var entries = new List<(EntryDescriptor Entry, byte[] Data)>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files.OrderBy(file => file.Id, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(file.Id) || file.Id != file.Id.Trim()
                || string.IsNullOrWhiteSpace(file.Path) || file.Path != file.Path.Trim().Replace('\\', '/'))
                throw new InvalidDataException("PAK resources require canonical, non-empty identity and path.");
            if (file.Id != file.Metadata.Id || file.Path != file.Metadata.Path || file.TypeId != file.Metadata.TypeId)
                throw new InvalidDataException("PAK entry identity does not match its typed metadata.");
            if (!ids.Add(file.Id) || !paths.Add(AssetPathHelper.Normalize(file.Path)))
                throw new InvalidDataException("Duplicate PAK resource ID or logical path.");
            var bytes = file.ReadAllBytes();
            if (bytes.LongLength != file.Length) throw new InvalidDataException("Resource size changed while preparing PAK.");
            ValidateCompiledDependencies(new GameFile(file.Id, file.Path, file.TypeId, file.Metadata, bytes));
            var actualCompression = compression != CompressionMode.None && bytes.Length > 256 ? compression : CompressionMode.None;
            var stored = actualCompression == CompressionMode.None ? bytes : CompressionHelper.Compress(bytes, actualCompression);
            entries.Add((new(file.Metadata, 0, bytes.Length, stored.Length, actualCompression, CryptoHelper.HashSHA256(bytes)), stored));
        }
        ValidateTargets(descriptor, ids);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Utf8, leaveOpen: true);
        writer.Write("GPAK"u8);
        writer.Write(CurrentVersion);
        WriteString(writer, JsonCodec.Serialize(descriptor, ControlOptions));
        writer.Write(entries.Count);
        long offset = 0;
        foreach (var (entry, stored) in entries)
        {
            WriteString(writer, entry.Metadata.Id);
            WriteString(writer, entry.Metadata.Path);
            WriteString(writer, entry.Metadata.TypeId);
            WriteString(writer, codec.Serialize(entry.Metadata));
            writer.Write(offset);
            writer.Write(entry.OriginalLength);
            writer.Write(entry.StoredLength);
            writer.Write((int)entry.Compression);
            writer.Write(Utf8.GetBytes(entry.Hash));
            offset += stored.Length;
        }
        foreach (var (_, stored) in entries) writer.Write(stored);
        writer.Flush();
        return stream.ToArray();
    }

    private static void ValidateCompiledDependencies(GameFile file)
    {
        if (file.TypeId != ImageVariantsCodec.ResourceType) return;
        var definition = ImageVariantsCodec.Decode(file.ReadAllBytes());
        var expected = definition.Variants.Select(variant => variant.AssetId.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (file.Metadata is not IAssetDependencies source) throw new InvalidDataException("Compiled image variants require dependency metadata.");
        var info = source.GetDependencies();
        if (!info.Complete || info.Direct.Any(item => item is null || string.IsNullOrWhiteSpace(item.AssetId)
                || item.ExpectedTypeId != "sprite" || item.RequiredStoryPackage is not null)
            || !expected.SetEquals(info.Direct.Select(item => item.AssetId.Trim())))
            throw new InvalidDataException("Compiled image variants dependencies do not match their payload.");
    }

    private static void ValidateTargets(PakDescriptor descriptor, IEnumerable<string> ids)
    {
        var available = ids.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (descriptor.Mappings.Values.Any(id => !available.Contains(id)))
            throw new InvalidDataException("Override mapping targets must be carried by their PAK.");
    }

    private static string ReadString(BinaryReader reader) => Utf8.GetString(ReadExact(reader, reader.ReadInt32()));
    private static byte[] ReadExact(BinaryReader reader, int count)
    {
        if (count < 0 || count > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("Invalid PAK field length.");
        var bytes = reader.ReadBytes(count);
        if (bytes.Length != count) throw new InvalidDataException("Truncated PAK field.");
        return bytes;
    }
    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Utf8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private sealed record EntryDescriptor(AssetMeta Metadata, long Offset, long OriginalLength, long StoredLength,
        CompressionMode Compression, string Hash);
}
