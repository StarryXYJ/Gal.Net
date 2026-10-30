using GalNet.Assets;
using GalNet.Assets.Provider;
using GalNet.Assets.Variants;
using GalNet.Core.Assets;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Services.Interfaces;

namespace GalNet.Editor.Services;

/// <summary>ID-indexed editor resources. A generation shares one index and outlives its active readers and leases.</summary>
public sealed class EditorAssetManager : IAssetManager, IEditorAssetChanges
{
    private readonly IProjectService _projects;
    private readonly IAssetCatalogService _catalog;
    private readonly IResourceTypeCatalog _resourceTypes;
    private readonly object _gate = new();
    private readonly Dictionary<(string, Type), Action<AssetManager>> _decoders = [];
    private readonly List<SharedProvider> _providers = [];
    private Generation _current = new([], []);
    private long _revision;
    private bool _disposed;
    public event Action? Changed;
    public long Revision { get { lock (_gate) return _revision; } }

    public EditorAssetManager(IProjectService projects, IAssetCatalogService catalog, IResourceTypeCatalog resourceTypes)
    {
        _projects = projects; _catalog = catalog; _resourceTypes = resourceTypes;
        _projects.CurrentChanged += OnProjectChanged; _catalog.Changed += OnCatalogChanged;
        Rebuild();
    }
    private void OnProjectChanged(Abstraction.Project.GalProject? project) => Rebuild();
    private void OnCatalogChanged() => Rebuild();
    private void Rebuild()
    {
        Generation previous;
        lock (_gate)
        {
            if (_disposed) return;
            var providers = new List<IAssetProvider>();
            if (_projects.Current is { } project) providers.Add(new LocalFileProvider(project.AssetsPath, _resourceTypes, optional: true));
            providers.AddRange(_providers.Select(provider => provider.Borrow()));
            previous = _current;
            _current = new(providers, _decoders.Values);
            _revision++;
        }
        Release(previous);
        Changed?.Invoke();
    }
    private Generation Retain()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _current.Readers++;
            return _current;
        }
    }
    private void Release(Generation generation)
    {
        bool dispose;
        lock (_gate) dispose = --generation.Readers == 0;
        if (dispose) generation.Manager.Dispose();
    }
    private async Task<AssetIndex> IndexAsync(Generation generation, CancellationToken ct)
    {
        Task<AssetIndex> task;
        lock (_gate)
        {
            if (generation.Index is null || generation.Index.IsFaulted || generation.Index.IsCanceled)
            {
                generation.Readers++;
                generation.Index = Task.Run(() => LoadIndexAsync(generation));
            }
            task = generation.Index;
        }
        return await task.WaitAsync(ct).ConfigureAwait(false);
    }
    private async Task<AssetIndex> LoadIndexAsync(Generation generation)
    {
        try
        {
            var files = new Dictionary<string, IGameFile>(StringComparer.OrdinalIgnoreCase);
            foreach (var provider in generation.Providers)
            {
                if (!provider.Exists("assets")) continue;
                using var archive = await provider.OpenArchiveAsync("assets").ConfigureAwait(false);
                foreach (var id in archive.AssetIds)
                    if (archive.GetAsset(id) is { } file && !files.TryAdd(file.Id, file))
                        throw new InvalidDataException($"Duplicate resource ID '{file.Id}'.");
            }
            return new(files, files.Values.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase).ToArray());
        }
        finally { Release(generation); }
    }
    public async Task<IGameFile?> GetFileAsync(string assetId, CancellationToken ct = default)
    {
        var generation = Retain();
        try { return string.IsNullOrWhiteSpace(assetId) ? null : (await IndexAsync(generation, ct).ConfigureAwait(false)).ById.GetValueOrDefault(assetId.Trim()); }
        finally { Release(generation); }
    }
    public async Task<IReadOnlyList<IGameFile>> GetFilesAsync(string? typeId = null, CancellationToken ct = default)
    {
        var generation = Retain();
        try
        {
            var index = await IndexAsync(generation, ct).ConfigureAwait(false);
            return typeId is null ? index.Files : index.Files.Where(file => file.TypeId.Equals(typeId.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        }
        finally { Release(generation); }
    }
    public Task<AssetHandle<T>?> AcquireAsync<T>(string assetId, CancellationToken ct = default) where T : class
        => AcquireAsync<T>(manager => manager.AcquireAsync<T>(assetId, ct));
    public Task<AssetHandle<T>?> AcquireByPathAsync<T>(string path, CancellationToken ct = default) where T : class
        => AcquireAsync<T>(manager => manager.AcquireByPathAsync<T>(path, ct));
    private async Task<AssetHandle<T>?> AcquireAsync<T>(Func<AssetManager, Task<AssetHandle<T>?>> acquire) where T : class
    {
        var generation = Retain();
        var transferred = false;
        try
        {
            var handle = await acquire(generation.Manager).ConfigureAwait(false);
            if (handle is null) return null;
            var result = new AssetHandle<T>(handle.AssetId, handle.TypeId, handle.Value, () => { try { handle.Dispose(); } finally { Release(generation); } });
            transferred = true;
            return result;
        }
        finally { if (!transferred) Release(generation); }
    }
    public void RegisterDecoder<T>(string resourceTypeId, IAssetDecoder<T> decoder) where T : class
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _current.Manager.RegisterDecoder(resourceTypeId, decoder);
            _decoders[(resourceTypeId.Trim().ToLowerInvariant(), typeof(T))] = manager => manager.RegisterDecoder(resourceTypeId, decoder);
        }
    }
    public void RegisterProvider(IAssetProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        lock (_gate) { ObjectDisposedException.ThrowIf(_disposed, this); _providers.Add(new(provider)); }
        Rebuild();
    }
    public void Dispose()
    {
        Generation current;
        SharedProvider[] providers;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _projects.CurrentChanged -= OnProjectChanged; _catalog.Changed -= OnCatalogChanged;
            current = _current; Changed = null;
            providers = _providers.ToArray();
            _providers.Clear();
        }
        Release(current);
        foreach (var provider in providers) provider.Release();
    }
    private sealed record AssetIndex(IReadOnlyDictionary<string, IGameFile> ById, IReadOnlyList<IGameFile> Files);
    private sealed class Generation
    {
        public Generation(IReadOnlyList<IAssetProvider> providers, IEnumerable<Action<AssetManager>> decoders)
        {
            Providers = providers; Manager = new(providers); Manager.RegisterImageVariantsDecoder();
            Manager.Diagnostic += diagnostic => Serilog.Log.ForContext<EditorAssetManager>().Warning(diagnostic.Exception,
                "Asset {AssetStage} failed ({AssetFailure}): ID={AssetId}, Path={AssetPath}, Provider={AssetProvider}, SourceID={SourceAssetId}, Target={AssetTargetType}",
                diagnostic.Stage, diagnostic.Kind, diagnostic.AssetId, diagnostic.Path, diagnostic.Provider, diagnostic.SourceAssetId, diagnostic.TargetType?.FullName);
            foreach (var register in decoders) register(Manager);
        }
        public AssetManager Manager { get; }
        public IReadOnlyList<IAssetProvider> Providers { get; }
        public Task<AssetIndex>? Index { get; set; }
        public int Readers { get; set; } = 1;
    }
    private sealed class SharedProvider(IAssetProvider provider)
    {
        private int _references = 1;
        public BorrowedProvider Borrow() { Interlocked.Increment(ref _references); return new BorrowedProvider(provider, Release); }
        public void Release() { if (Interlocked.Decrement(ref _references) == 0 && provider is IDisposable disposable) disposable.Dispose(); }
    }
    private sealed class BorrowedProvider(IAssetProvider provider, Action release) : IAssetProvider, IDisposable
    {
        private Action? _release = release;
        public string Name => provider.Name;
        public bool Exists(string name) => provider.Exists(name);
        public IArchive OpenArchive(string name) => provider.OpenArchive(name);
        public Task<IArchive> OpenArchiveAsync(string name, CancellationToken ct = default) => provider.OpenArchiveAsync(name, ct);
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
