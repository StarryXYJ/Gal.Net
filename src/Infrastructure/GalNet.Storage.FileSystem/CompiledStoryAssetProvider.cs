using System.Collections.Frozen;
using GalNet.Assets;
using GalNet.Core.Assets;

namespace GalNet.Storage.FileSystem;

/// <summary>Adapts development build output to the same reserved story IDs as installed PAKs.</summary>
public sealed class CompiledStoryAssetProvider : IAssetProvider
{
    private readonly string _root;
    private readonly Lazy<FrozenDictionary<string, IGameFile>> _snapshot;
    public CompiledStoryAssetProvider(string root)
    {
        _root = Path.GetFullPath(root);
        _snapshot = new(() => CompiledStoryResources.Create(File.ReadAllBytes(Path.Combine(_root, "Graph", "graph.json")), id =>
        {
            var path = FileSystemPaths.ResolveRelativeFile(_root, "Graph/groups/" + id + ".galgroup");
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }).ToFrozenDictionary(file => file.Id, StringComparer.OrdinalIgnoreCase));
    }
    public string Name => "CompiledStory(" + _root + ")";
    public bool Exists(string archiveName) => File.Exists(Path.Combine(_root, "Graph", "graph.json"));
    public IArchive OpenArchive(string archiveName) => new StoryArchive(archiveName, _snapshot.Value);
    public Task<IArchive> OpenArchiveAsync(string archiveName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(OpenArchive(archiveName));
    }

    private sealed class StoryArchive(string name, FrozenDictionary<string, IGameFile> files) : IArchive
    {
        private bool _disposed;
        public string Name => name;
        public IEnumerable<string> AssetIds => files.Keys;
        public bool Contains(string assetId) => files.ContainsKey(assetId.Trim());
        public IGameFile? GetAsset(string assetId)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return files.GetValueOrDefault(assetId.Trim());
        }
        public IGameFile? GetAssetByPath(string path) => files.Values.FirstOrDefault(file =>
            string.Equals(file.Path, path.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
        public void Dispose() => _disposed = true;
    }
}
