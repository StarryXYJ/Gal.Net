using GalNet.Core.Assets;

namespace GalNet.Assets.Provider;

public sealed record ResourceFileSource(IGameFile Index, IAssetProvider Provider);
public sealed record ResourceOverrideSource(PakDescriptor Descriptor, string Sha256, IReadOnlyList<ResourceFileSource> Files);

/// <summary>A frozen logical resource view. Mapped files retain the original ID/path; source PAKs remain explicit.</summary>
public sealed class ResourceOverlayProvider : IAssetProvider
{
    private readonly Dictionary<string, IGameFile> _files;
    public string Name => "ResourceOverlay";

    public ResourceOverlayProvider(IReadOnlyList<ResourceFileSource> baseline, IReadOnlyList<ResourceOverrideSource> overrides,
        IResourceTypeCatalog types)
    {
        var codec = new AssetMetaCodec(types);
        var originals = baseline.GroupBy(item => item.Index.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        _files = originals.ToDictionary(pair => pair.Key, pair => (IGameFile)new SourceFile(pair.Value), StringComparer.OrdinalIgnoreCase);
        var selected = new Dictionary<string, (ResourceOverrideSource Pack, ResourceFileSource Target)>(StringComparer.OrdinalIgnoreCase);
        foreach (var patch in overrides.DistinctBy(patch => patch.Sha256, StringComparer.Ordinal)
                     .OrderByDescending(patch => patch.Descriptor.Priority).ThenByDescending(patch => patch.Sha256, StringComparer.Ordinal))
        {
            patch.Descriptor.Validate();
            if (patch.Descriptor.Kind != "override") throw new InvalidDataException("Only override PAKs contribute resource mappings.");
            var targets = patch.Files.ToDictionary(item => item.Index.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var (source, target) in patch.Descriptor.Mappings)
            {
                if (!targets.TryGetValue(target, out var replacement)) throw new InvalidDataException("Override mapping target is not carried by its PAK.");
                if (!originals.TryGetValue(source, out var original)) continue; // An absent story remains absent.
                ResourceOverrideBuilder.ValidateCompatibility(new SourceFile(original), new SourceFile(replacement));
                selected.TryAdd(source, (patch, replacement));
            }
        }
        foreach (var (source, selection) in selected)
        {
            var original = originals[source].Index;
            var metadata = codec.Deserialize(codec.Serialize(selection.Target.Index.Metadata), selection.Target.Index.Path);
            metadata.Id = original.Id;
            metadata.Path = original.Path;
            _files[source] = new SourceFile(selection.Target, original.Id, original.Path, metadata);
        }
        // Only active packages' carried dependencies are made addressable. Existing IDs never change implicitly.
        foreach (var patch in selected.Values.Select(value => value.Pack).DistinctBy(patch => patch.Sha256))
        {
            foreach (var file in patch.Files)
            {
                if (file.Index.TypeId is "graph" or "group") throw new InvalidDataException("Overrides cannot carry story resources.");
                if (file.Index.Metadata is not IAssetDependencies dependencies) continue;
                foreach (var dependency in dependencies.GetDependencies().Direct)
                {
                    var carried = patch.Files.FirstOrDefault(item => StringComparer.OrdinalIgnoreCase.Equals(item.Index.Id, dependency.AssetId))
                        ?? throw new InvalidDataException("Override dependency must be carried by the same PAK.");
                    if (_files.TryGetValue(carried.Index.Id, out var existing))
                    {
                        // Explicit mappings are independent requests; baseline data must otherwise agree.
                        var comparison = originals.TryGetValue(carried.Index.Id, out var original) ? original.Index : existing;
                        if (comparison.Hash != carried.Index.Hash || comparison.Length != carried.Index.Length
                            || codec.Serialize(comparison.Metadata) != codec.Serialize(carried.Index.Metadata))
                            throw new InvalidDataException($"Carried dependency '{carried.Index.Id}' conflicts with another source. Assign the changed author resource a new ID.");
                    }
                    else _files.Add(carried.Index.Id, new SourceFile(carried));
                }
            }
            foreach (var carried in patch.Files)
                _files.TryAdd(carried.Index.Id, new SourceFile(carried));
        }
    }

    public bool Exists(string archiveName) => archiveName is "assets" or "assets.pak";
    public IArchive OpenArchive(string archiveName) => Exists(archiveName) ? new View(_files) : throw new FileNotFoundException(archiveName);
    public Task<IArchive> OpenArchiveAsync(string archiveName, CancellationToken ct = default)
    { ct.ThrowIfCancellationRequested(); return Task.FromResult(OpenArchive(archiveName)); }
    private sealed class View(Dictionary<string, IGameFile> files) : IArchive
    {
        public string Name => "assets";
        public IEnumerable<string> AssetIds => files.Keys;
        public bool Contains(string id) => files.ContainsKey(id.Trim());
        public IGameFile? GetAsset(string id) => files.GetValueOrDefault(id.Trim());
        public IGameFile? GetAssetByPath(string path) => files.Values.FirstOrDefault(file => StringComparer.OrdinalIgnoreCase.Equals(file.Path, AssetPathHelper.Normalize(path)));
        public void Dispose() { }
    }
    private sealed class SourceFile(ResourceFileSource source, string? id = null, string? path = null, AssetMeta? metadata = null) : IGameFile, IAssetOverrideFile
    {
        public bool IsResourceOverride => id is not null;
        public string Id => id ?? source.Index.Id;
        public string Path => path ?? source.Index.Path;
        public string TypeId => source.Index.TypeId;
        public AssetMeta Metadata => metadata ?? source.Index.Metadata;
        public long Length => source.Index.Length;
        public string? Hash => source.Index.Hash;
        public Stream OpenRead() => new MemoryStream(ReadAllBytes(), writable: false);
        public byte[] ReadAllBytes()
        {
            using var archive = source.Provider.OpenArchive("assets");
            return (archive.GetAsset(source.Index.Id) ?? throw new InvalidDataException("Prepared resource disappeared.")).ReadAllBytes();
        }
        public async Task<byte[]> ReadAllBytesAsync(CancellationToken ct = default)
        {
            using var archive = await source.Provider.OpenArchiveAsync("assets", ct);
            return await (archive.GetAsset(source.Index.Id) ?? throw new InvalidDataException("Prepared resource disappeared.")).ReadAllBytesAsync(ct);
        }
    }
}
