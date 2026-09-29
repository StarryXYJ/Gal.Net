using System.Text.Json;
using GalNet.Assets.Provider;
using GalNet.Core.Assets;

namespace GalNet.Assets.Tests;

public sealed class AssetPipelineTests
{
    [Test]
    public void Pak_roundtrip_preserves_registered_meta_dto_and_type_id()
    {
        var meta = new SpriteAssetMeta { Id = "opening", TypeId = "sprite", Path = "cg/opening.png", Filter = "nearest" };
        var original = new GameFile(meta.Id, meta.Path, meta.TypeId, meta, "png"u8.ToArray());

        var pak = Archive.Serialize("assets", [original]);
        using var archive = Archive.Deserialize("assets", pak);
        var loaded = archive.GetAsset("opening");

        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.TypeId, Is.EqualTo("sprite"));
        Assert.That(loaded.Metadata, Is.TypeOf<SpriteAssetMeta>());
        Assert.That(((SpriteAssetMeta)loaded.Metadata).Filter, Is.EqualTo("nearest"));
        Assert.That(loaded.ReadAllBytes(), Is.EqualTo("png"u8.ToArray()));
    }

    [Test]
    public async Task Local_provider_deserializes_typed_meta()
    {
        var root = Path.Combine(Path.GetTempPath(), $"galnet-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "cg"));
        try
        {
            var resourcePath = Path.Combine(root, "cg", "opening.png");
            await File.WriteAllBytesAsync(resourcePath, "png"u8.ToArray());
            var meta = new SpriteAssetMeta { Id = "opening", TypeId = "sprite", Path = "cg/opening.png", Filter = "bilinear" };
            await File.WriteAllTextAsync(resourcePath + ".meta", JsonSerializer.Serialize(meta));
            using var provider = new LocalFileProvider(root);
            using var archive = await provider.OpenArchiveAsync("assets");

            var file = archive.GetAsset("opening");
            Assert.That(file, Is.Not.Null);
            Assert.That(file!.Metadata, Is.TypeOf<SpriteAssetMeta>());
            Assert.That(((SpriteAssetMeta)file.Metadata).Filter, Is.EqualTo("bilinear"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task Local_provider_infers_missing_type_and_path_and_preserves_unknown_fields()
    {
        var root = Path.Combine(Path.GetTempPath(), $"galnet-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "cg"));
        try
        {
            var resourcePath = Path.Combine(root, "cg", "opening.png");
            await File.WriteAllBytesAsync(resourcePath, "png"u8.ToArray());
            await File.WriteAllTextAsync(resourcePath + ".meta", """{ "id": "opening", "futureField": true }""");
            using var provider = new LocalFileProvider(root);
            using var archive = await provider.OpenArchiveAsync("assets");

            var file = archive.GetAsset("opening");
            Assert.That(file!.TypeId, Is.EqualTo("sprite"));
            Assert.That(file.Path, Is.EqualTo("cg/opening.png"));
            Assert.That(file.Metadata.ExtensionData!["futureField"].GetBoolean(), Is.True);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task Local_provider_reports_invalid_metadata_instead_of_skipping_the_asset()
    {
        var root = Path.Combine(Path.GetTempPath(), $"galnet-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(root, "broken.png"), "png"u8.ToArray());
            await File.WriteAllTextAsync(Path.Combine(root, "broken.png.meta"), "{ invalid json");
            using var provider = new LocalFileProvider(root);

            var exception = Assert.ThrowsAsync<InvalidDataException>(() => provider.OpenArchiveAsync("assets"));
            Assert.That(exception!.Message, Does.Contain("broken.png.meta"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task Decoder_dispatches_by_resource_type_and_target_type()
    {
        var sprite = new GameFile("sprite", "a.png", "sprite", new SpriteAssetMeta { Id = "sprite", TypeId = "sprite", Path = "a.png" }, "sprite"u8.ToArray());
        var audio = new GameFile("audio", "a.ogg", "audio", new AudioAssetMeta { Id = "audio", TypeId = "audio", Path = "a.ogg" }, "audio"u8.ToArray());
        using var manager = new AssetManager([new InlineProvider(sprite, audio)]);
        manager.RegisterDecoder<Decoded>("sprite", new ConstantDecoder("image"));
        manager.RegisterDecoder<Decoded>("audio", new ConstantDecoder("sound"));

        using var spriteHandle = await manager.AcquireAsync<Decoded>("sprite");
        using var audioHandle = await manager.AcquireAsync<Decoded>("audio");
        Assert.That(spriteHandle?.Value.Value, Is.EqualTo("image"));
        Assert.That(audioHandle?.Value.Value, Is.EqualTo("sound"));
    }

    [Test]
    public async Task Asset_handles_own_independent_references()
    {
        var file = new GameFile("sprite", "a.png", "sprite", new SpriteAssetMeta { Id = "sprite", TypeId = "sprite", Path = "a.png" }, "sprite"u8.ToArray());
        using var manager = new AssetManager([new InlineProvider(file)]);
        manager.RegisterDecoder<DisposableAsset>("sprite", new DisposableDecoder());

        var first = await manager.AcquireAsync<DisposableAsset>("sprite");
        var second = await manager.AcquireAsync<DisposableAsset>("sprite");

        Assert.That(second!.Value, Is.SameAs(first!.Value));
        first.Dispose();
        Assert.That(first.Value.Disposed, Is.False);

        using var third = await manager.AcquireAsync<DisposableAsset>("sprite");
        Assert.That(third!.Value, Is.SameAs(first.Value));

        second.Dispose();
        Assert.That(first.Value.Disposed, Is.False);
        third.Dispose();
        Assert.That(first.Value.Disposed, Is.True);
    }

    [Test]
    public async Task Concurrent_acquires_share_one_load_and_waiter_cancellation_is_local()
    {
        var file = new GameFile("sprite", "a.png", "sprite", new SpriteAssetMeta { Id = "sprite", TypeId = "sprite", Path = "a.png" }, "sprite"u8.ToArray());
        using var manager = new AssetManager([new InlineProvider(file)]);
        var decoder = new ControlledDecoder();
        manager.RegisterDecoder<DisposableAsset>("sprite", decoder);
        using var cancellation = new CancellationTokenSource();

        var cancelledAcquire = manager.AcquireAsync<DisposableAsset>("sprite", cancellation.Token);
        await decoder.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var successfulAcquire = manager.AcquireAsync<DisposableAsset>("sprite");
        cancellation.Cancel();
        decoder.Release.TrySetResult();

        Assert.That(async () => await cancelledAcquire, Throws.InstanceOf<OperationCanceledException>());
        using var handle = await successfulAcquire;
        Assert.Multiple(() =>
        {
            Assert.That(handle, Is.Not.Null);
            Assert.That(decoder.DecodeCount, Is.EqualTo(1));
            Assert.That(handle!.Value.Disposed, Is.False);
        });

        var asset = handle!.Value;
        handle.Dispose();
        Assert.That(asset.Disposed, Is.True);
    }

    [Test]
    public async Task Dispose_cancels_an_in_flight_load_and_rejects_future_acquires()
    {
        var file = new GameFile("sprite", "a.png", "sprite", new SpriteAssetMeta { Id = "sprite", TypeId = "sprite", Path = "a.png" }, "sprite"u8.ToArray());
        var manager = new AssetManager([new InlineProvider(file)]);
        var decoder = new CancellationAwareDecoder();
        manager.RegisterDecoder<DisposableAsset>("sprite", decoder);
        var acquire = manager.AcquireAsync<DisposableAsset>("sprite");
        await decoder.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        manager.Dispose();

        Assert.That(async () => await acquire, Throws.InstanceOf<OperationCanceledException>());
        await decoder.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.ThrowsAsync<ObjectDisposedException>(async () => await manager.AcquireAsync<DisposableAsset>("sprite"));
    }

    private sealed record Decoded(string Value);

    private sealed class DisposableAsset : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private sealed class ConstantDecoder(string result) : IAssetDecoder<Decoded>
    {
        public ValueTask<Decoded?> DecodeAsync(IGameFile file, ReadOnlyMemory<byte> data, CancellationToken ct = default) =>
            ValueTask.FromResult<Decoded?>(new Decoded(result));
    }

    private sealed class DisposableDecoder : IAssetDecoder<DisposableAsset>
    {
        public ValueTask<DisposableAsset?> DecodeAsync(IGameFile file, ReadOnlyMemory<byte> data, CancellationToken ct = default) =>
            ValueTask.FromResult<DisposableAsset?>(new DisposableAsset());
    }

    private sealed class ControlledDecoder : IAssetDecoder<DisposableAsset>
    {
        private int _decodeCount;

        public int DecodeCount => _decodeCount;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<DisposableAsset?> DecodeAsync(
            IGameFile file,
            ReadOnlyMemory<byte> data,
            CancellationToken ct = default)
        {
            Interlocked.Increment(ref _decodeCount);
            Started.TrySetResult();
            await Release.Task.WaitAsync(ct);
            return new DisposableAsset();
        }
    }

    private sealed class CancellationAwareDecoder : IAssetDecoder<DisposableAsset>
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<DisposableAsset?> DecodeAsync(
            IGameFile file,
            ReadOnlyMemory<byte> data,
            CancellationToken ct = default)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return new DisposableAsset();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                Cancelled.TrySetResult();
                throw;
            }
        }
    }

    private sealed class InlineProvider(params IGameFile[] files) : IAssetProvider
    {
        public string Name => "inline";
        public bool Exists(string archiveName) => archiveName == "assets";
        public IArchive OpenArchive(string archiveName) => new InlineArchive(files);
        public Task<IArchive> OpenArchiveAsync(string archiveName, CancellationToken ct = default) => Task.FromResult<IArchive>(new InlineArchive(files));
    }

    private sealed class InlineArchive(IEnumerable<IGameFile> files) : IArchive
    {
        private readonly Dictionary<string, IGameFile> _files = files.ToDictionary(file => file.Id);
        public string Name => "assets";
        public IEnumerable<string> AssetIds => _files.Keys;
        public bool Contains(string assetId) => _files.ContainsKey(assetId);
        public IGameFile? GetAsset(string assetId) => _files.GetValueOrDefault(assetId);
        public IGameFile? GetAssetByPath(string path) => _files.Values.SingleOrDefault(file => file.Path == path);
        public void Dispose() { }
    }
}
