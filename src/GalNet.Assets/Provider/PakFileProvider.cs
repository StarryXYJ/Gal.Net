using GalNet.Core.Assets;

namespace GalNet.Assets.Provider;

/// <summary>
/// 打包模式资源提供者 —— 从 .pak 文件加载已打包资源。
/// </summary>
public sealed class PakFileProvider : IAssetProvider
{
    private readonly string _pakDirectory;
    private readonly string? _pakPath;
    private readonly string? _archiveName;
    private readonly bool _optional;
    private readonly IResourceTypeCatalog _resourceTypes;

    public PakFileProvider(string pakDirectory, IResourceTypeCatalog? resourceTypes = null, bool optional = false)
    {
        _pakDirectory = pakDirectory.Replace('\\', '/').TrimEnd('/');
        _optional = optional;
        _resourceTypes = resourceTypes ?? BuiltinResourceTypes.CreateCatalog();
    }

    /// <summary>Maps one explicitly named archive to a PAK file, for example a patch PAK.</summary>
    public PakFileProvider(string pakPath, string archiveName, IResourceTypeCatalog? resourceTypes = null, bool optional = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pakPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(archiveName);
        _pakDirectory = string.Empty;
        _pakPath = Path.GetFullPath(pakPath);
        _archiveName = NormalizeArchiveName(archiveName);
        _optional = optional;
        _resourceTypes = resourceTypes ?? BuiltinResourceTypes.CreateCatalog();
    }

    public string Name => $"PakFile({_pakPath ?? _pakDirectory})";

    public bool Exists(string archiveName)
    {
        return TryGetPakPath(archiveName, out var path) && File.Exists(path);
    }

    public IArchive OpenArchive(string archiveName)
    {
        if (!TryGetPakPath(archiveName, out var path) || !File.Exists(path))
        {
            if (_optional)
                return new EmptyArchive(archiveName);
            throw new FileNotFoundException($"Pak file not found: {path}");
        }

        var data = File.ReadAllBytes(path);
        return Archive.Deserialize(archiveName, data, _resourceTypes);
    }

    public async Task<IArchive> OpenArchiveAsync(string archiveName, CancellationToken ct = default)
    {
        if (!TryGetPakPath(archiveName, out var path) || !File.Exists(path))
        {
            if (_optional)
                return new EmptyArchive(archiveName);
            throw new FileNotFoundException($"Pak file not found: {path}");
        }

        var data = await File.ReadAllBytesAsync(path, ct);
        return Archive.Deserialize(archiveName, data, _resourceTypes);
    }

    private bool TryGetPakPath(string archiveName, out string path)
    {
        if (_pakPath is not null)
        {
            path = _pakPath;
            return string.Equals(_archiveName, NormalizeArchiveName(archiveName), StringComparison.OrdinalIgnoreCase);
        }
        var name = archiveName.EndsWith(".pak", StringComparison.OrdinalIgnoreCase)
            ? archiveName
            : $"{archiveName}.pak";
        path = $"{_pakDirectory}/{name}";
        return true;
    }

    private static string NormalizeArchiveName(string archiveName) =>
        archiveName.EndsWith(".pak", StringComparison.OrdinalIgnoreCase) ? archiveName[..^4] : archiveName;

    /// <summary>空归档 —— 当提供者为 optional 时使用。</summary>
    private sealed class EmptyArchive : IArchive
    {
        public string Name { get; }
        public IEnumerable<string> AssetIds => [];
        public EmptyArchive(string name) => Name = name;
        public bool Contains(string assetId) => false;
        public IGameFile? GetAsset(string assetId) => null;
        public IGameFile? GetAssetByPath(string path) => null;
        public void Dispose() { }
    }
}
