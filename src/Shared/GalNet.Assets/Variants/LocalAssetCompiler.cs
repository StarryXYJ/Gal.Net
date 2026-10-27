using GalNet.Contracts.Builtins.Variants;
using GalNet.Core.Assets;

namespace GalNet.Assets.Variants;

/// <summary>Prepares a caller-owned file snapshot for both local archives and publication preflight.</summary>
public static class LocalAssetCompiler
{
    public static async Task<IReadOnlyList<IGameFile>> CompileAsync(IReadOnlyDictionary<string, string> files,
        IReadOnlyCollection<string> directories, IResourceTypeCatalog resourceTypes, CancellationToken ct = default)
    {
        var codec = new AssetMetaCodec(resourceTypes);
        var assets = new Dictionary<string, IGameFile>(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in files.Where(item => item.Key.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)).OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var path = pair.Key[..^5];
            try
            {
                var meta = codec.Deserialize(await File.ReadAllTextAsync(pair.Value, ct).ConfigureAwait(false), path);
                meta.Path = path;
                if (!files.TryGetValue(path, out var backingFile)) throw new InvalidDataException("Metadata has no adjacent resource file.");
                if (!ids.Add(meta.Id)) throw new InvalidDataException($"Duplicate resource ID '{meta.Id}'.");
                assets.Add(path, new SourceFile(meta, backingFile));
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException or KeyNotFoundException)
            {
                throw new InvalidDataException($"Failed to load asset metadata '{pair.Key}': {exception.Message}", exception);
            }
        }
        // Indexing never repairs missing sidecars. A descriptor without identity must not disappear silently.
        foreach (var path in files.Keys.Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)))
            if (resourceTypes.TryGetByExtension(Path.GetExtension(path), out var type) && type.TypeId == ImageVariantsCodec.ResourceType && !assets.ContainsKey(path))
                throw new InvalidDataException($"Image variants resource '{path}' has no metadata.");

        var directorySet = directories.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var original in assets.Values.Where(file => file.TypeId == ImageVariantsCodec.ResourceType).ToArray())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var source = ImageVariantsCodec.ReadSource(await original.ReadAllBytesAsync(ct).ConfigureAwait(false));
                var folder = ResolveFolder(original.Path, source.Folder);
                if (!directorySet.Contains(folder)) throw new InvalidDataException($"Directory '{source.Folder}' does not exist.");
                var facts = new List<ImageVariantFile>();
                foreach (var path in files.Keys.Where(path => Parent(path).Equals(folder, StringComparison.OrdinalIgnoreCase)
                    && !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal))
                {
                    var isSpriteExtension = resourceTypes.TryGetByExtension(Path.GetExtension(path), out var registration) && registration.TypeId == "sprite";
                    assets.TryGetValue(path, out var asset);
                    if (!isSpriteExtension && asset?.TypeId != "sprite") continue;
                    if (asset is null) throw new InvalidDataException($"Image '{path}' has no metadata.");
                    facts.Add(new ImageVariantFile(path[(path.LastIndexOf('/') + 1)..], asset.Id, asset.TypeId));
                }
                var definition = ImageVariantsCodec.RequireDefinition(ImageVariantsBuilder.Build(source, facts));
                assets[original.Path] = new GameFile(original.Id, original.Path, original.TypeId, original.Metadata, ImageVariantsCodec.Encode(definition));
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
            {
                throw new InvalidDataException($"Invalid image variants '{original.Path}': {exception.Message}", exception);
            }
        }
        ct.ThrowIfCancellationRequested();
        return assets.Values.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Resolves an author directory in the virtual Assets namespace; never accesses the disk.</summary>
    public static string ResolveFolder(string descriptorPath, string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) throw new InvalidDataException("An image variants folder is required.");
        folder = folder.Replace('\\', '/');
        if (folder.StartsWith('/') || folder.Contains(':')) throw new InvalidDataException("Image variants folder must be relative to its descriptor.");
        var parts = Parent(descriptorPath).Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        foreach (var part in folder.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            if (part == "..")
            {
                if (parts.Count == 0) throw new InvalidDataException("Image variants folder escapes Assets.");
                parts.RemoveAt(parts.Count - 1);
            }
            else parts.Add(part);
        }
        return string.Join('/', parts);
    }

    private static string Parent(string path) => path.LastIndexOf('/') is var index && index >= 0 ? path[..index] : "";

    private sealed class SourceFile(AssetMeta meta, string backingFile) : IGameFile
    {
        public string Id => meta.Id;
        public string Path => meta.Path;
        public string TypeId => meta.TypeId;
        public AssetMeta Metadata => meta;
        public long Length => new FileInfo(backingFile).Length;
        public string? Hash => null;
        public Stream OpenRead() => File.OpenRead(backingFile);
        public byte[] ReadAllBytes() => File.ReadAllBytes(backingFile);
        public Task<byte[]> ReadAllBytesAsync(CancellationToken ct = default) => File.ReadAllBytesAsync(backingFile, ct);
    }
}
