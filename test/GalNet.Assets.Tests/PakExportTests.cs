using GalNet.Contracts.Builtins.Assets;
using GalNet.Core.Assets;

namespace GalNet.Assets.Tests;

public sealed class PakExportTests
{
    private static readonly IResourceTypeCatalog Types = BuiltinResourceTypes.CreateCatalog();
    private string _root = null!;

    [SetUp]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), $"galnet-pak-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void Cleanup() => Directory.Delete(_root, recursive: true);

    [Test]
    public void File_and_buffered_exports_are_identical_and_roundtrip([Values] CompressionMode compression, [Values] bool empty)
    {
        var random = new Random(42);
        IGameFile[] files = empty ? [] : [Create("末尾", 2048), Create("b", 256), Create("a", 257), Create("empty", 0)];
        var descriptor = new PakDescriptor
        {
            Kind = "override", PackageId = "extra", Priority = 3,
            Mappings = empty ? [] : new() { ["z-original"] = "末尾", ["a-original"] = "a" }
        };
        var bytes = PakBuilder.Build("assets", files, Types, compression, descriptor);
        if (compression == CompressionMode.None && !empty)
            Assert.That(CryptoHelper.HashSHA256(bytes), Is.EqualTo("1dd00f74e4ef2aa1d977fa7a7c015ea080a024919cfb7c4a6a4658b4d3093654"),
                "Keep the wire bytes captured before P3c, including metadata, table offsets and hashes.");
        var path = Path.Combine(_root, "assets.pak");
        // Existing longer contents must be truncated on successful replacement.
        File.WriteAllBytes(path, new byte[bytes.Length + 100]);
        PakBuilder.BuildToFile(path, files.Reverse().ToArray(), Types, compression, descriptor);
        Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes));
        using var archive = Archive.Deserialize("assets", bytes, Types);
        Assert.That(archive.AssetIds.Count(), Is.EqualTo(files.Length));
        Assert.That(archive.Descriptor.Mappings, Is.EquivalentTo(descriptor.Mappings));
        foreach (var file in files)
            Assert.That(archive.GetAsset(file.Id)!.ReadAllBytes(), Is.EqualTo(file.ReadAllBytes()));

        GameFile Create(string id, int length)
        {
            var data = new byte[length];
            random.NextBytes(data);
            var metadata = new AssetMeta { Id = id, Path = $"资源/{id}.bin", TypeId = "data" };
            return new GameFile(id, metadata.Path, metadata.TypeId, metadata, data, new string('0', 64));
        }
    }

    [TestCase("duplicate")]
    [TestCase("metadata")]
    [TestCase("mapping")]
    [TestCase("read")]
    [TestCase("length")]
    public void Preparation_failure_does_not_touch_existing_target(string failure)
    {
        var path = Path.Combine(_root, "existing.pak");
        var sentinel = "existing contents"u8.ToArray();
        File.WriteAllBytes(path, sentinel);
        var metadata = new AssetMeta { Id = "a", Path = "a.bin", TypeId = "data" };
        IGameFile good = new GameFile("a", "a.bin", "data", metadata, [1, 2, 3]);
        IGameFile[] files = failure switch
        {
            "duplicate" => [good, good],
            "metadata" => [new GameFile("other", "a.bin", "data", metadata, [1])],
            "read" or "length" => [new BrokenFile(good, failure == "read")],
            _ => [good]
        };
        var descriptor = failure == "mapping"
            ? new PakDescriptor { Kind = "override", PackageId = "extra", Mappings = new() { ["source"] = "missing" } }
            : new PakDescriptor();
        Assert.That(() => PakBuilder.BuildToFile(path, files, Types, descriptor: descriptor), Throws.Exception);
        Assert.That(File.ReadAllBytes(path), Is.EqualTo(sentinel));
        Assert.That(Directory.GetFiles(_root), Has.Length.EqualTo(1));
    }

    private sealed class BrokenFile(IGameFile source, bool throwOnRead) : IGameFile
    {
        public string Id => source.Id;
        public string Path => source.Path;
        public string TypeId => source.TypeId;
        public AssetMeta Metadata => source.Metadata;
        public long Length => source.Length + 1;
        public string? Hash => null;
        public Stream OpenRead() => throw new NotSupportedException();
        public byte[] ReadAllBytes() => throwOnRead ? throw new IOException("Source failed.") : source.ReadAllBytes();
        public Task<byte[]> ReadAllBytesAsync(CancellationToken ct = default) => Task.FromResult(ReadAllBytes());
    }
}
