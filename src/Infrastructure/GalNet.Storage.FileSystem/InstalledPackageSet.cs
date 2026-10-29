using System.Text.Json;
using GalNet.Assets;
using GalNet.Assets.Provider;
using GalNet.Core.Assets;
using GalNet.Core.Serialization;

namespace GalNet.Storage.FileSystem;

/// <summary>A validated immutable package selection. Providers verify those same bytes on each later open.</summary>
internal sealed class InstalledPackageSet
{
    private sealed record Pak(string Path, string Hash, string PackageId, IReadOnlyList<IGameFile> Index);
    private readonly List<Pak> _paks = [];
    private readonly List<ResourceOverrideSource> _overrides = [];
    private readonly IResourceTypeCatalog _types;
    public GameContentManifest Manifest { get; }
    public PackedGamePackage? GamePackage { get; private init; }
    public PackedDocumentSet? Localization { get; private set; }
    public HashSet<string> InstalledStories { get; } = new(StringComparer.Ordinal);
    private InstalledPackageSet(GameContentManifest manifest, IResourceTypeCatalog types) { Manifest = manifest; _types = types; }
    public IReadOnlyList<IAssetProvider> CreateProviders() => [new ResourceOverlayProvider(_paks.SelectMany(pak =>
        pak.Index.Select(file => new ResourceFileSource(file, new PakFileProvider(pak.Path, "assets", _types, expectedSha256: pak.Hash)))).ToArray(), _overrides, _types)];

    public static InstalledPackageSet Load(string root, IResourceTypeCatalog types)
    {
        var packedPath = Path.Combine(root, PackedGameDocuments.GameFileName);
        var packed = File.Exists(packedPath) ? PackedGameDocuments.ReadGame(File.ReadAllBytes(packedPath)) : null;
        var path = Path.Combine(root, GameContentManifest.FileName);
        if (packed is null && !File.Exists(path)) throw new InvalidDataException("Installed content requires game.pak or a legacy content.json. Re-export the game.");
        var manifest = packed?.Manifest ?? JsonCodec.Deserialize<GameContentManifest>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web)
        { AllowDuplicateProperties = false, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow })
            ?? throw new InvalidDataException("Missing content manifest.");
        if (manifest.Version != (packed is null ? 2 : 3) || manifest.GraphId != StoryResourceIds.Graph || string.IsNullOrWhiteSpace(manifest.ProjectId)
            || !Hash(manifest.BuildId) || manifest.BasePaks is not { Count: > 0 } || manifest.Stories is null)
            throw new InvalidDataException("Unsupported or incomplete content manifest.");
        var result = new InstalledPackageSet(manifest, types) { GamePackage = packed };
        if (packed is not null)
        {
            var expected = manifest.LocalizationPak!;
            var bytes = File.ReadAllBytes(Path.Combine(root, PackedGameDocuments.LocalizationFileName));
            if (bytes.LongLength != expected.Size || CryptoHelper.HashSHA256(bytes) != expected.Sha256)
                throw new InvalidDataException("Packed localization failed integrity verification.");
            result.Localization = PackedGameDocuments.Read(bytes);
            if (result.Localization.Descriptor.ProjectId != manifest.ProjectId || result.Localization.Descriptor.BuildId != manifest.BuildId)
                throw new InvalidDataException("Packed localization belongs to another content build.");
        }
        var filePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < manifest.BasePaks.Count; index++)
        {
            var expected = manifest.BasePaks[index];
            if (expected?.Path is null || !expected.Path.StartsWith("Assets/Paks/", StringComparison.Ordinal) || !filePaths.Add(expected.Path))
                throw new InvalidDataException("Invalid or duplicate base PAK path.");
            result._paks.Add(result.Read(FileSystemPaths.ResolveRelativeFile(root, expected.Path), expected, "base", "", index, manifest.BasePaks.Count));
        }
        var definitions = new Dictionary<string, StoryPackageManifest>(StringComparer.Ordinal);
        foreach (var story in manifest.Stories)
        {
            if (story is null || string.IsNullOrWhiteSpace(story.PackageId) || story.Parts is not { Count: > 0 } || !definitions.TryAdd(story.PackageId, story))
                throw new InvalidDataException("Invalid story package definition.");
            foreach (var part in story.Parts)
                if (part?.Path is null || !part.Path.StartsWith("StoryPatches/" + manifest.BuildId + "/", StringComparison.Ordinal)
                    || !filePaths.Add(part.Path) || !Hash(part.Sha256) || part.Size < 0)
                    throw new InvalidDataException("Invalid story part descriptor.");
        }
        var installed = new Dictionary<(string Package, int Index), Pak>();
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        var roots = new[] { Path.Combine(root, "StoryPatches", manifest.BuildId),
            Path.Combine(Path.GetDirectoryName(Path.GetFullPath(root)) ?? root, "StoryPatches", manifest.BuildId) }.Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in roots.Where(Directory.Exists))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Story package directories cannot be links.");
            foreach (var candidate in Directory.EnumerateFiles(directory, "*.pak").Order(StringComparer.Ordinal))
            {
                var bytes = File.ReadAllBytes(candidate);
                var hash = CryptoHelper.HashSHA256(bytes);
                if (!hashes.Add(hash)) continue;
                using var archive = Archive.Deserialize("assets", bytes, types);
                var control = archive.Descriptor;
                if (control.Kind != "story" || !definitions.TryGetValue(control.PackageId, out var definition)
                    || control.PartIndex >= definition.Parts.Count)
                    throw new InvalidDataException("Unrecognized story PAK in the current build's package directory.");
                var pak = result.Read(candidate, definition.Parts[control.PartIndex], "story", control.PackageId,
                    control.PartIndex, definition.Parts.Count, bytes);
                if (!installed.TryAdd((control.PackageId, control.PartIndex), pak)) throw new InvalidDataException("Conflicting story package parts.");
            }
        }
        foreach (var story in manifest.Stories)
        {
            var own = installed.Where(pair => pair.Key.Package == story.PackageId).OrderBy(pair => pair.Key.Index).Select(pair => pair.Value).ToArray();
            if (own.Length == 0) continue;
            if (own.Length != story.Parts.Count) throw new InvalidDataException($"Story package '{story.PackageId}' is only partially installed.");
            result.InstalledStories.Add(story.PackageId);
            result._paks.AddRange(own);
        }
        result.ValidateDependencies();
        result.LoadOverrides(root);
        return result;
    }

    private void LoadOverrides(string root)
    {
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        var directories = new[] { Path.Combine(root, "ResourcePatches"),
            Path.Combine(Path.GetDirectoryName(Path.GetFullPath(root)) ?? root, "ResourcePatches") }.Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in directories.Where(Directory.Exists))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Resource patch directory cannot be a link.");
            foreach (var path in Directory.EnumerateFiles(directory, "*.pak").Order(StringComparer.Ordinal))
            {
                var bytes = File.ReadAllBytes(path);
                var hash = CryptoHelper.HashSHA256(bytes);
                if (!hashes.Add(hash)) continue;
                using var archive = Archive.Deserialize("assets", bytes, _types);
                var descriptor = archive.Descriptor;
                if (descriptor.Kind != "override" || descriptor.ProjectId != Manifest.ProjectId || descriptor.BuildId.Length != 0)
                    throw new InvalidDataException("Resource override PAK has the wrong kind/project or binds a base build.");
                foreach (var id in archive.AssetIds) _ = archive.GetAsset(id)!.ReadAllBytes();
                var files = archive.MetadataIndex.Select(entry => (IGameFile)new IndexFile(entry)).ToArray();
                if (files.Any(file => file.TypeId is "graph" or "group")) throw new InvalidDataException("Resource overrides cannot carry graph/group resources.");
                _ = AssetDependencyCollector.Collect(files, files.Select(file => file.Id), _types, declaredComplete: files.Select(file => file.Id));
                var provider = new PakFileProvider(path, "assets", _types, expectedSha256: hash);
                _overrides.Add(new(descriptor, hash, files.Select(file => new ResourceFileSource(file, provider)).ToArray()));
            }
        }
        // Resolve once during preparation, so invalid mappings cannot be swallowed as a presentation fallback.
        _ = CreateProviders();
    }

    private Pak Read(string path, GalpakFileEntry expected, string kind, string packageId, int index, int count, byte[]? bytes = null)
    {
        if (!Hash(expected.Sha256) || expected.Size < 0) throw new InvalidDataException("Invalid PAK checksum descriptor.");
        if (!File.Exists(path)) throw new InvalidDataException($"Required PAK '{expected.Path}' is missing.");
        bytes ??= File.ReadAllBytes(path);
        if (bytes.LongLength != expected.Size || CryptoHelper.HashSHA256(bytes) != expected.Sha256)
            throw new InvalidDataException($"PAK '{expected.Path}' failed integrity verification.");
        using var archive = Archive.Deserialize("assets", bytes, _types);
        var control = archive.Descriptor;
        if (control.Kind != kind || control.ProjectId != Manifest.ProjectId || control.BuildId != Manifest.BuildId
            || control.PackageId != packageId || control.PartIndex != index || control.PartCount != count)
            throw new InvalidDataException($"PAK '{expected.Path}' does not belong to the declared content build or part set.");
        return new(path, expected.Sha256, packageId, archive.MetadataIndex.Select(entry => (IGameFile)new IndexFile(entry)).ToArray());
    }

    private void ValidateDependencies()
    {
        var all = new Dictionary<string, IGameFile>(StringComparer.OrdinalIgnoreCase);
        var codec = new AssetMetaCodec(_types);
        foreach (var file in _paks.SelectMany(pak => pak.Index))
        {
            if (all.TryGetValue(file.Id, out var previous))
            {
                if (previous.Hash != file.Hash || previous.Length != file.Length || codec.Serialize(previous.Metadata) != codec.Serialize(file.Metadata))
                    throw new InvalidDataException($"Story/base packages contain different versions of resource '{file.Id}'.");
            }
            else all.Add(file.Id, file);
        }
        var baseline = _paks.Where(pak => pak.PackageId.Length == 0).SelectMany(pak => pak.Index).DistinctBy(file => file.Id, StringComparer.OrdinalIgnoreCase).ToArray();
        _ = AssetDependencyCollector.Collect(baseline, baseline.Select(file => file.Id), _types, declaredComplete: baseline.Select(file => file.Id));
        foreach (var package in InstalledStories)
        {
            var own = _paks.Where(pak => pak.PackageId == package).SelectMany(pak => pak.Index).ToArray();
            var available = baseline.Concat(own).DistinctBy(file => file.Id, StringComparer.OrdinalIgnoreCase).ToArray();
            _ = AssetDependencyCollector.Collect(available, own.Select(file => file.Id), _types, package, available.Select(file => file.Id));
        }
    }

    private static bool Hash(string value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private sealed class IndexFile(PakAssetIndexEntry entry) : IGameFile
    {
        public string Id => entry.Id;
        public string Path => entry.Path;
        public string TypeId => entry.TypeId;
        public AssetMeta Metadata => entry.Metadata;
        public long Length => entry.Length;
        public string? Hash => entry.Hash;
        public Stream OpenRead() => throw new InvalidOperationException("This is a metadata-only dependency index.");
        public byte[] ReadAllBytes() => throw new InvalidOperationException("This is a metadata-only dependency index.");
        public Task<byte[]> ReadAllBytesAsync(CancellationToken ct = default) => throw new InvalidOperationException("This is a metadata-only dependency index.");
    }
}
