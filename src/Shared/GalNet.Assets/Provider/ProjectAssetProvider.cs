using GalNet.Core.Assets;

namespace GalNet.Assets.Provider;

/// <summary>Combines the two project resource roots, rejecting ambiguous identities and paths.</summary>
public sealed class ProjectAssetProvider : IAssetProvider, IDisposable
{
    private readonly LocalFileProvider[] _roots;
    public ProjectAssetProvider(string projectRoot, IResourceTypeCatalog resourceTypes)
    {
        _roots = [new(Path.Combine(projectRoot, "Assets"), resourceTypes, optional: true),
            new(Path.Combine(projectRoot, "Buildin"), resourceTypes, optional: true)];
    }
    public string Name => "Project assets";
    public bool Exists(string archiveName) => _roots.Any(root => root.Exists(archiveName));
    public IArchive OpenArchive(string archiveName) => OpenArchiveAsync(archiveName).GetAwaiter().GetResult();
    public async Task<IArchive> OpenArchiveAsync(string archiveName, CancellationToken ct = default)
    {
        var archives = new List<IArchive>();
        try
        {
            foreach (var root in _roots) archives.Add(await root.OpenArchiveAsync(archiveName, ct).ConfigureAwait(false));
            return new ProjectArchive(archiveName, archives, ct);
        }
        catch { foreach (var archive in archives) archive.Dispose(); throw; }
    }
    public void Dispose() { foreach (var root in _roots) root.Dispose(); }

    private sealed class ProjectArchive : IArchive
    {
        private readonly List<IArchive> _archives;
        private readonly Dictionary<string, IGameFile> _byId = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IGameFile> _byPath = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _idOrigins = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _pathOrigins = new(StringComparer.OrdinalIgnoreCase);
        private bool _disposed;
        public ProjectArchive(string name, List<IArchive> archives, CancellationToken ct)
        {
            Name = name; _archives = archives;
            for (var index = 0; index < archives.Count; index++)
            {
                var archive = archives[index];
                foreach (var id in archive.AssetIds)
                {
                    ct.ThrowIfCancellationRequested();
                    var file = archive.GetAsset(id, ct) ?? throw new InvalidDataException($"Missing resource '{id}'.");
                    var key = Key(file.Id); var path = AssetPathHelper.Normalize(file.Path);
                    var origin = $"{(index == 0 ? "Assets" : "Buildin")}/{file.Path}";
                    if (!_byId.TryAdd(key, file)) throw new InvalidDataException($"Duplicate project resource ID '{file.Id}' in '{_idOrigins[key]}' and '{origin}'.");
                    _idOrigins.Add(key, origin);
                    if (!_byPath.TryAdd(path, file)) throw new InvalidDataException($"Duplicate project resource path '{file.Path}' in '{_pathOrigins[path]}' and '{origin}'.");
                    _pathOrigins.Add(path, origin);
                }
            }
        }
        public string Name { get; }
        private static string Key(string id) => Guid.TryParse(id, out var guid) ? guid.ToString("N") : id;
        public IEnumerable<string> AssetIds => _byId.Values.Select(file => file.Id);
        public bool Contains(string assetId) { ObjectDisposedException.ThrowIf(_disposed, this); return _byId.ContainsKey(Key(assetId)); }
        public IGameFile? GetAsset(string assetId, CancellationToken ct = default)
        { ct.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(_disposed, this); return _byId.GetValueOrDefault(Key(assetId)); }
        public IGameFile? GetAssetByPath(string path, CancellationToken ct = default)
        { ct.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(_disposed, this); return _byPath.GetValueOrDefault(AssetPathHelper.Normalize(path)); }
        public void Dispose()
        { if (_disposed) return; _disposed = true; foreach (var archive in _archives) archive.Dispose(); _byId.Clear(); _byPath.Clear(); }
    }
}
