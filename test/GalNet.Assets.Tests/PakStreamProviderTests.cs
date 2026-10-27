using GalNet.Assets.Provider;
using GalNet.Contracts.Builtins.Assets;
using GalNet.Core.Assets;

namespace GalNet.Assets.Tests;

public sealed class PakStreamProviderTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task Non_seekable_stream_is_owned_and_closed_before_returning_an_independent_archive(bool asynchronous)
    {
        var bytes = CreatePak();
        var streams = new List<ReadStream>();
        var provider = new PakStreamProvider(new Dictionary<string, Func<Stream>>
        {
            ["assets.pak"] = () => { var stream = new ReadStream(bytes); streams.Add(stream); return stream; }
        }, BuiltinResourceTypes.CreateCatalog());

        Assert.That(provider.Exists("ASSETS"), Is.True);
        Assert.That(provider.Exists("missing"), Is.False);
        Assert.That(streams, Is.Empty, "Manifest lookup must not open the resource.");
        using var archive = asynchronous ? await provider.OpenArchiveAsync("assets") : provider.OpenArchive("assets");
        using var second = await provider.OpenArchiveAsync("ASSETS.PAK");
        Assert.That(streams, Has.Count.EqualTo(2));
        Assert.That(streams.All(stream => stream.Closed), Is.True);
        Assert.That(archive.GetAsset("clip")!.ReadAllBytes(), Is.EqualTo(new byte[] { 1, 2, 3 }));
        Assert.That(second.GetAsset("clip")!.Path, Is.EqualTo("音频/clip.ogg"));
    }

    [Test]
    public async Task Cancellation_during_copy_disposes_stream_and_does_not_poison_next_open()
    {
        using var cancellation = new CancellationTokenSource();
        var canceled = new ReadStream(CreatePak(), () => cancellation.Cancel());
        var calls = 0;
        var provider = new PakStreamProvider(new Dictionary<string, Func<Stream>>
        {
            ["assets"] = () => ++calls == 1 ? canceled : new ReadStream(CreatePak())
        }, BuiltinResourceTypes.CreateCatalog());
        Assert.That(() => provider.OpenArchiveAsync("assets", cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
        Assert.That(canceled.Closed, Is.True);
        using var archive = await provider.OpenArchiveAsync("assets");
        Assert.That(archive.Contains("clip"), Is.True);
        Assert.That(() => provider.OpenArchiveAsync("assets", cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
        Assert.That(calls, Is.EqualTo(2), "Pre-canceled requests must not open a stream.");
    }

    [Test]
    public void Missing_corrupt_and_duplicate_archives_are_reported_without_leaking_streams()
    {
        var stream = new ReadStream("invalid PAK"u8.ToArray());
        var provider = new PakStreamProvider(new Dictionary<string, Func<Stream>> { ["assets"] = () => stream }, BuiltinResourceTypes.CreateCatalog());
        Assert.Throws<FileNotFoundException>(() => provider.OpenArchive("missing"));
        Assert.ThrowsAsync<InvalidDataException>(() => provider.OpenArchiveAsync("assets"));
        Assert.That(stream.Closed, Is.True);
        Assert.Throws<ArgumentException>(() => new PakStreamProvider(new Dictionary<string, Func<Stream>>
        {
            ["assets"] = () => Stream.Null,
            ["ASSETS.pak"] = () => Stream.Null
        }, BuiltinResourceTypes.CreateCatalog()));
    }

    [Test]
    public async Task Packaged_asset_materializes_under_host_cache_and_survives_owner_disposal_until_last_lease()
    {
        var root = Path.Combine(Path.GetTempPath(), $"galnet-packaged-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var sentinel = Path.Combine(root, "host-owned.txt");
        await File.WriteAllTextAsync(sentinel, "keep");
        try
        {
            var provider = new PakStreamProvider(new Dictionary<string, Func<Stream>> { ["assets"] = () => new ReadStream(CreatePak()) }, BuiltinResourceTypes.CreateCatalog());
            using var assets = new AssetManager([provider]);
            await using var files = new AssetFileMaterializer(assets, root);
            using var first = await files.AcquireAsync("clip");
            using var second = await files.AcquireAsync("clip");
            Assert.That(Path.GetRelativePath(root, first.Path), Does.StartWith("GalNet" + Path.DirectorySeparatorChar));
            Assert.That(first.Path, Is.EqualTo(second.Path));
            Assert.That(await File.ReadAllBytesAsync(first.Path), Is.EqualTo(new byte[] { 1, 2, 3 }));
            await files.DisposeAsync();
            first.Dispose();
            Assert.That(File.Exists(second.Path), Is.True);
            second.Dispose();
            Assert.That(Directory.Exists(Path.GetDirectoryName(second.Path)), Is.False);
            Assert.That(File.Exists(sentinel), Is.True);
        }
        finally { Directory.Delete(root, true); }
    }

    private static byte[] CreatePak()
    {
        var meta = new AssetMeta { Id = "clip", TypeId = "audio", Path = "音频/clip.ogg" };
        return Archive.Serialize("assets", [new GameFile(meta.Id, meta.Path, meta.TypeId, meta, [1, 2, 3])], BuiltinResourceTypes.CreateCatalog());
    }

    private sealed class ReadStream(byte[] bytes, Action? onRead = null) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);
        public bool Closed { get; private set; }
        public override bool CanRead => !Closed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            onRead?.Invoke();
            return _inner.ReadAsync(buffer, cancellationToken);
        }
        protected override void Dispose(bool disposing) { Closed = true; if (disposing) _inner.Dispose(); base.Dispose(disposing); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
