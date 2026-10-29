using GalNet.Core.Assets;

namespace GalNet.Assets.Provider;

/// <summary>Opens PAKs from a host-supplied manifest, including packaged, non-seekable resources.</summary>
public sealed class PakStreamProvider : IAssetProvider
{
    private readonly Dictionary<string, Func<Stream>> _archives = new(StringComparer.OrdinalIgnoreCase);
    private readonly IResourceTypeCatalog _resourceTypes;

    /// <param name="archives">Each factory transfers a fresh readable stream to the provider on every call.</param>
    /// <param name="resourceTypes">The same frozen catalog used to build the PAKs.</param>
    public PakStreamProvider(IReadOnlyDictionary<string, Func<Stream>> archives, IResourceTypeCatalog resourceTypes)
    {
        ArgumentNullException.ThrowIfNull(archives);
        _resourceTypes = resourceTypes ?? throw new ArgumentNullException(nameof(resourceTypes));
        foreach (var (name, open) in archives)
        {
            ArgumentNullException.ThrowIfNull(open);
            if (!_archives.TryAdd(NormalizeName(name), open))
                throw new ArgumentException($"Duplicate PAK archive name '{name}'.", nameof(archives));
        }
    }

    public string Name => "PakStream";

    /// <summary>Checks the explicit manifest; does not open a stream or enumerate storage.</summary>
    public bool Exists(string archiveName) => _archives.ContainsKey(NormalizeName(archiveName));

    public IArchive OpenArchive(string archiveName)
    {
        using var stream = Open(archiveName);
        using var data = new MemoryStream();
        stream.CopyTo(data);
        return Archive.Deserialize(archiveName, data.ToArray(), _resourceTypes);
    }

    public async Task<IArchive> OpenArchiveAsync(string archiveName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await using var stream = Open(archiveName);
        using var data = new MemoryStream();
        await stream.CopyToAsync(data, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return Archive.Deserialize(archiveName, data.ToArray(), _resourceTypes);
    }

    private Stream Open(string archiveName)
    {
        if (!_archives.TryGetValue(NormalizeName(archiveName), out var open))
            throw new FileNotFoundException($"PAK archive '{archiveName}' is not in the resource manifest.");
        return open() ?? throw new InvalidDataException($"PAK stream factory for '{archiveName}' returned null.");
    }

    private static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var normalized = name.EndsWith(".pak", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        ArgumentException.ThrowIfNullOrWhiteSpace(normalized);
        return normalized;
    }
}
