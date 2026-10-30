using BenchmarkDotNet.Attributes;
using GalNet.Assets.Provider;
using GalNet.Contracts.Builtins.Assets;
using GalNet.Core.Assets;

namespace GalNet.Assets.Benchmarks;

/// <summary>Steady-state PAK operations; OS file cache is not flushed.</summary>
[MemoryDiagnoser]
public class PakBenchmarks : IDisposable
{
    private const int SmallAssetBytes = 4096;
    private static readonly string ScratchRoot = Path.Combine(Path.GetTempPath(), "GalNet.AssetBenchmarks");
    private readonly string _root = Path.Combine(ScratchRoot, Guid.NewGuid().ToString("N"));
    private readonly IResourceTypeCatalog _types = BuiltinResourceTypes.CreateCatalog();
    private IGameFile[] _files = [];
    private byte[] _pak = [];
    private PakFileProvider _provider = null!;
    private AssetManager _assets = null!;
    private AssetHandle<byte[]>? _held;
    private string OutputPath => Path.Combine(_root, "export.pak");

    [Params(16, 64)]
    public int PayloadMiB { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        Directory.CreateDirectory(_root);
        var random = new Random(42);
        _files = Enumerable.Range(0, 16).Select(index => CreateFile($"bulk-{index:D2}", PayloadMiB * 1024 * 1024 / 16, random))
            .Concat(Enumerable.Range(0, 4).Select(index => CreateFile($"small-{index}", SmallAssetBytes, random))).ToArray();
        _pak = PakBuilder.Build("assets", _files, _types);
        var path = Path.Combine(_root, "input.pak");
        File.WriteAllBytes(path, _pak);
        _provider = new PakFileProvider(path, "assets", _types, expectedSha256: CryptoHelper.HashSHA256(_pak));
        using var archive = Archive.Deserialize("assets", _pak, _types);
        var sources = archive.MetadataIndex.Select(entry => new ResourceFileSource(
            _files.Single(file => file.Id == entry.Id), _provider)).ToArray();
        _assets = new AssetManager([new ResourceOverlayProvider(sources, [], _types)]);
        _held = _assets.AcquireAsync<byte[]>("small-0").GetAwaiter().GetResult();
    }

    [Benchmark]
    public int OpenMemoryIndex()
    {
        using var archive = Archive.Deserialize("assets", _pak, _types);
        return archive.AssetIds.Count();
    }

    [Benchmark]
    public int ReadOneVerified()
    {
        using var archive = _provider.OpenArchive("assets");
        return archive.GetAsset("small-1")!.ReadAllBytes().Length;
    }

    [Benchmark]
    public async Task<int> ReadFourVerified()
    {
        var total = 0;
        for (var index = 0; index < 4; index++)
        {
            // Separate IDs with no retained handles: exercise the installed-content overlay path.
            using var asset = await _assets.AcquireAsync<byte[]>($"bulk-{index:D2}");
            total += asset!.Value.Length;
        }
        return total;
    }

    [Benchmark]
    public async Task<int> HeldHandleCacheHit()
    {
        using var asset = await _assets.AcquireAsync<byte[]>("small-0");
        return asset!.Value.Length;
    }

    [Benchmark]
    public byte[] ExportBuffered() => PakBuilder.Build("assets", _files, _types);

    [Benchmark]
    public void ExportToFile() => PakBuilder.BuildToFile(OutputPath, _files, _types);

    public void Verify()
    {
        if (OpenMemoryIndex() != _files.Length || ReadOneVerified() != SmallAssetBytes ||
            ReadFourVerified().GetAwaiter().GetResult() != PayloadMiB * 1024 * 1024 / 4 ||
            HeldHandleCacheHit().GetAwaiter().GetResult() != SmallAssetBytes)
            throw new InvalidDataException("Benchmark lookup returned unexpected content.");
        if (!ExportBuffered().AsSpan().SequenceEqual(_pak))
            throw new InvalidDataException("Buffered export changed deterministic PAK bytes.");
        ExportToFile();
        if (!File.ReadAllBytes(OutputPath).AsSpan().SequenceEqual(_pak))
            throw new InvalidDataException("File export differs from the buffered PAK.");
        using var archive = _provider.OpenArchive("assets");
        foreach (var file in _files)
            if (!archive.GetAsset(file.Id)!.ReadAllBytes().AsSpan().SequenceEqual(file.ReadAllBytes()))
                throw new InvalidDataException($"Resource '{file.Id}' changed during its roundtrip.");
    }

    [GlobalCleanup]
    public void Dispose()
    {
        _held?.Dispose();
        _assets?.Dispose();
        // This instance only owns its freshly generated child directory.
        if (Path.GetDirectoryName(Path.GetFullPath(_root)) != Path.GetFullPath(ScratchRoot))
            throw new InvalidOperationException("Benchmark scratch path escaped its root.");
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static GameFile CreateFile(string id, int length, Random random)
    {
        var bytes = new byte[length];
        random.NextBytes(bytes); // Models already-compressed media; avoids unrealistic all-zero compression wins.
        var meta = new AssetMeta { Id = id, Path = id + ".bin", TypeId = "data" };
        return new GameFile(id, meta.Path, meta.TypeId, meta, bytes);
    }
}
