using GalNet.Core.Assets;

namespace GalNet.Assets.Provider;

/// <summary>
/// 打包模式资源提供者 —— 从 .pak 文件加载已打包资源。
/// </summary>
public sealed class PakFileProvider : IAssetProvider
{
    private readonly string _pakDirectory;
    private readonly bool _optional;

    public PakFileProvider(string pakDirectory, bool optional = false)
    {
        _pakDirectory = pakDirectory.Replace('\\', '/').TrimEnd('/');
        _optional = optional;
    }

    public string Name => $"PakFile({_pakDirectory})";

    public bool Exists(string archiveName)
    {
        var path = GetPakPath(archiveName);
        return File.Exists(path);
    }

    public IArchive OpenArchive(string archiveName)
    {
        var path = GetPakPath(archiveName);
        if (!File.Exists(path))
        {
            if (_optional)
                return new EmptyArchive(archiveName);
            throw new FileNotFoundException($"Pak file not found: {path}");
        }

        var data = File.ReadAllBytes(path);
        return Archive.Deserialize(archiveName, data);
    }

    public async Task<IArchive> OpenArchiveAsync(string archiveName, CancellationToken ct = default)
    {
        var path = GetPakPath(archiveName);
        if (!File.Exists(path))
        {
            if (_optional)
                return new EmptyArchive(archiveName);
            throw new FileNotFoundException($"Pak file not found: {path}");
        }

        var data = await File.ReadAllBytesAsync(path, ct);
        return Archive.Deserialize(archiveName, data);
    }

    private string GetPakPath(string archiveName)
    {
        var name = archiveName.EndsWith(".pak", StringComparison.OrdinalIgnoreCase)
            ? archiveName
            : $"{archiveName}.pak";
        return $"{_pakDirectory}/{name}";
    }

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
