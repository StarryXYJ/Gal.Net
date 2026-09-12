using GalNet.Core.Assets;

namespace GalNet.Assets.Tests;

public sealed class AssetManagerTests
{
    [Test]
    public async Task LoadAsync_CacheHit_ReturnsCachedInstance()
    {
        using var manager = new AssetManager();
        var provider = new MockProvider("test-archive", new GameFile("id-1", "file.txt", ResourceType.Unknown, "data"u8.ToArray()));
        manager.RegisterProvider(provider);

        var first = await manager.LoadAsync<byte[]>("id-1");
        var second = await manager.LoadAsync<byte[]>("id-1");

        Assert.That(second, Is.Not.Null);
        Assert.That(second, Is.SameAs(first)); // Same cached instance
        Assert.That(provider.LoadCount, Is.EqualTo(1)); // Only loaded once
    }

    [Test]
    public async Task LoadAsync_ConcurrentRequests_ShareOneDecodeAndOwnSeparateRefs()
    {
        using var manager = new AssetManager();
        var provider = new MockProvider("test", new GameFile("id-1", "file.bin", ResourceType.Unknown, "data"u8.ToArray()));
        var decoder = new BlockingAssetDecoder();
        manager.RegisterProvider(provider);
        manager.RegisterDecoder(decoder);

        var firstTask = manager.LoadAsync<DisposableAsset>("id-1");
        await decoder.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var secondTask = manager.LoadAsync<DisposableAsset>("id-1");

        decoder.Release();
        var results = await Task.WhenAll(firstTask, secondTask);

        var firstResult = results[0]!;
        Assert.Multiple(() =>
        {
            Assert.That(results[1], Is.SameAs(firstResult));
            Assert.That(provider.LoadCount, Is.EqualTo(1));
            Assert.That(decoder.DecodeCount, Is.EqualTo(1));
        });

        manager.Release<DisposableAsset>("id-1");
        Assert.That(firstResult.Disposed, Is.False);
        manager.Release<DisposableAsset>("id-1");
        Assert.That(firstResult.Disposed, Is.True);
    }

    [Test]
    public async Task LoadAsync_CancellingOneWaiter_DoesNotCancelOtherWaiters()
    {
        using var manager = new AssetManager();
        var provider = new MockProvider("test", new GameFile("id-1", "file.bin", ResourceType.Unknown, "data"u8.ToArray()));
        var decoder = new BlockingAssetDecoder();
        manager.RegisterProvider(provider);
        manager.RegisterDecoder(decoder);

        using var firstCancellation = new CancellationTokenSource();
        var firstTask = manager.LoadAsync<DisposableAsset>("id-1", firstCancellation.Token);
        await decoder.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var secondTask = manager.LoadAsync<DisposableAsset>("id-1");

        firstCancellation.Cancel();
        Assert.That(async () => await firstTask, Throws.InstanceOf<OperationCanceledException>());

        decoder.Release();
        var second = await secondTask;
        Assert.That(second, Is.Not.Null);
        Assert.That(provider.LoadCount, Is.EqualTo(1));
        manager.Release<DisposableAsset>("id-1");
    }

    [Test]
    public async Task ClearCache_InvalidatesAnInFlightResultInsteadOfRepopulatingCache()
    {
        using var manager = new AssetManager();
        var provider = new MockProvider("test", new GameFile("id-1", "file.bin", ResourceType.Unknown, "data"u8.ToArray()));
        var decoder = new NonCooperativeBlockingAssetDecoder();
        manager.RegisterProvider(provider);
        manager.RegisterDecoder(decoder);

        var loadTask = manager.LoadAsync<DisposableAsset>("id-1");
        await decoder.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        manager.ClearCache();

        decoder.Release();
        Assert.That(async () => await loadTask, Throws.InstanceOf<OperationCanceledException>());
        await decoder.Completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(SpinWait.SpinUntil(() => decoder.Result?.Disposed == true, TimeSpan.FromSeconds(2)), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(manager.CachedCount, Is.EqualTo(0));
            Assert.That(decoder.Result!.Disposed, Is.True);
        });
    }

    [Test]
    public async Task LoadAsync_Release_RefCountDecays()
    {
        using var manager = new AssetManager();
        var provider = new MockProvider("test-archive", new GameFile("id-1", "file.txt", ResourceType.Unknown, "data"u8.ToArray()));
        manager.RegisterProvider(provider);

        Assert.That(manager.IsLoaded("id-1"), Is.False);

        await manager.LoadAsync<byte[]>("id-1");
        Assert.That(manager.IsLoaded("id-1"), Is.True);

        manager.Release("id-1");
        Assert.That(manager.IsLoaded("id-1"), Is.False);
    }

    [Test]
    public async Task LoadAsync_MultipleRefs_ReleaseAfterAllDrops()
    {
        using var manager = new AssetManager();
        var provider = new MockProvider("test-archive", new GameFile("id-1", "file.txt", ResourceType.Unknown, "data"u8.ToArray()));
        manager.RegisterProvider(provider);

        // Acquire 3 references
        var r1 = await manager.LoadAsync<byte[]>("id-1");
        var r2 = await manager.LoadAsync<byte[]>("id-1");
        var r3 = await manager.LoadAsync<byte[]>("id-1");

        Assert.That(provider.LoadCount, Is.EqualTo(1));
        Assert.That(manager.IsLoaded("id-1"), Is.True);

        // Release 2, still has 1 ref
        manager.Release("id-1");
        manager.Release("id-1");
        Assert.That(manager.IsLoaded("id-1"), Is.True);

        // Release last, should be removed
        manager.Release("id-1");
        Assert.That(manager.IsLoaded("id-1"), Is.False);
    }

    [Test]
    public async Task LoadAsync_UnknownId_ReturnsNull()
    {
        using var manager = new AssetManager();
        var provider = new MockProvider("test-archive", new GameFile("id-1", "file.txt", ResourceType.Unknown, "data"u8.ToArray()));
        manager.RegisterProvider(provider);

        var result = await manager.LoadAsync<byte[]>("nonexistent");
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task LoadAsync_NoProvider_ReturnsNull()
    {
        using var manager = new AssetManager();
        var result = await manager.LoadAsync<byte[]>("id-1");
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetFileAsync_FindsMetadataWithoutAddingCacheEntry()
    {
        using var manager = new AssetManager();
        manager.RegisterProvider(new MockProvider("test", new GameFile("id-image", "bg/title.png", ResourceType.Sprite, "data"u8.ToArray())));

        var file = await manager.GetFileAsync("id-image");

        Assert.That(file, Is.Not.Null);
        Assert.That(file!.Path, Is.EqualTo("bg/title.png"));
        Assert.That(manager.CachedCount, Is.EqualTo(0));
    }

    [Test]
    public async Task GetFilesAsync_FiltersByResourceTypeAndDeduplicatesIds()
    {
        using var manager = new AssetManager();
        manager.RegisterProvider(new MockProvider("first", new GameFile("id-image", "bg/first.png", ResourceType.Sprite, "data"u8.ToArray())));
        manager.RegisterProvider(new MockProvider("second", new GameFile("id-image", "bg/second.png", ResourceType.Sprite, "other"u8.ToArray())));

        var images = await manager.GetFilesAsync(ResourceType.Sprite);
        var audio = await manager.GetFilesAsync(ResourceType.Audio);

        Assert.That(images, Has.Count.EqualTo(1));
        Assert.That(images[0].Path, Is.EqualTo("bg/first.png"));
        Assert.That(audio, Is.Empty);
    }

    [Test]
    public async Task LoadAsync_AsString_ReturnsUtf8String()
    {
        using var manager = new AssetManager();
        var provider = new MockProvider("test-archive", new GameFile("id-1", "file.txt", ResourceType.Unknown, "你好 GalNet!"u8.ToArray()));
        manager.RegisterProvider(provider);

        var result = await manager.LoadAsync<string>("id-1");
        Assert.That(result, Is.EqualTo("你好 GalNet!"));
    }

    [Test]
    public async Task LoadAsync_AsGameFile_ReturnsGameFile()
    {
        using var manager = new AssetManager();
        var original = new GameFile("id-1", "file.txt", ResourceType.Unknown, "data"u8.ToArray());
        var provider = new MockProvider("test-archive", original);
        manager.RegisterProvider(provider);

        var result = await manager.LoadAsync<IGameFile>("id-1");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo("id-1"));
        Assert.That(result.ReadAllBytes(), Is.EqualTo("data"u8.ToArray()));
    }

    [Test]
    public async Task LoadAsync_WithResolvedFile_SkipsProviderLookup()
    {
        using var manager = new AssetManager();
        var file = new GameFile("id-1", "file.txt", ResourceType.Unknown, "data"u8.ToArray());
        var provider = new MockProvider("test-archive", file);
        manager.RegisterProvider(provider);

        var result = await manager.LoadAsync<byte[]>(file);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo("data"u8.ToArray()));
            Assert.That(provider.LoadCount, Is.Zero);
            Assert.That(manager.CachedCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task LoadAsync_CustomDecoder_IsTypedCachedAndReleasedByType()
    {
        using var manager = new AssetManager();
        manager.RegisterProvider(new MockProvider("test", new GameFile("id-1", "sprite.bin", ResourceType.Sprite, "data"u8.ToArray())));
        var decoder = new DisposableAssetDecoder();
        manager.RegisterDecoder(decoder);

        var first = await manager.LoadAsync<DisposableAsset>("id-1");
        var second = await manager.LoadAsync<DisposableAsset>("id-1");

        Assert.That(first, Is.Not.Null);
        Assert.That(second, Is.SameAs(first));
        Assert.That(decoder.DecodeCount, Is.EqualTo(1));
        Assert.That(manager.TryGetLoaded<DisposableAsset>("id-1", out var loaded), Is.True);
        Assert.That(loaded, Is.SameAs(first));

        manager.Release<DisposableAsset>("id-1");
        Assert.That(first!.Disposed, Is.False);
        manager.Release<DisposableAsset>("id-1");
        Assert.That(first.Disposed, Is.True);
        Assert.That(manager.TryGetLoaded<DisposableAsset>("id-1", out _), Is.False);
    }

    [Test]
    public async Task ClearCache_PreservesRegisteredDecoder()
    {
        using var manager = new AssetManager();
        manager.RegisterProvider(new MockProvider("test", new GameFile("id-1", "file.bin", ResourceType.Unknown, "data"u8.ToArray())));
        var decoder = new DisposableAssetDecoder();
        manager.RegisterDecoder(decoder);

        var first = await manager.LoadAsync<DisposableAsset>("id-1");
        manager.ClearCache();
        var second = await manager.LoadAsync<DisposableAsset>("id-1");

        Assert.That(first, Is.Not.SameAs(second));
        Assert.That(decoder.DecodeCount, Is.EqualTo(2));
        manager.Release<DisposableAsset>("id-1");
    }

    [Test]
    public async Task ClearCache_RemovesAllEntries()
    {
        using var manager = new AssetManager();
        var provider = new MockProvider("test-archive", new GameFile("id-1", "file.txt", ResourceType.Unknown, "data"u8.ToArray()));
        manager.RegisterProvider(provider);

        await manager.LoadAsync<byte[]>("id-1");
        Assert.That(manager.CachedCount, Is.EqualTo(1));

        manager.ClearCache();
        Assert.That(manager.CachedCount, Is.EqualTo(0));
        Assert.That(manager.IsLoaded("id-1"), Is.False);
    }

    [Test]
    public async Task MultipleProviders_SecondFallback_Works()
    {
        using var manager = new AssetManager();

        var provider1 = new MockProvider("archive1", new GameFile("id-1", "file1.txt", ResourceType.Unknown, "from-provider-1"u8.ToArray()));
        var provider2 = new MockProvider("archive2", new GameFile("id-2", "file2.txt", ResourceType.Unknown, "from-provider-2"u8.ToArray()));

        manager.RegisterProvider(provider1);
        manager.RegisterProvider(provider2);

        var r1 = await manager.LoadAsync<byte[]>("id-1");
        Assert.That(r1, Is.EqualTo("from-provider-1"u8.ToArray()));

        var r2 = await manager.LoadAsync<byte[]>("id-2");
        Assert.That(r2, Is.EqualTo("from-provider-2"u8.ToArray()));
    }

    [Test]
    public void Dispose_PreventsFurtherOperations()
    {
        var manager = new AssetManager();
        manager.Dispose();

        Assert.That(() => manager.LoadAsync<byte[]>("id-1"), Throws.TypeOf<ObjectDisposedException>());
        Assert.That(() => manager.Release("id-1"), Throws.TypeOf<ObjectDisposedException>());
    }

    // ── LoadByPathAsync ──

    [Test]
    public async Task LoadByPathAsync_FindsAssetByPath()
    {
        using var manager = new AssetManager();
        var provider = new MockProvider("test", new GameFile("id-bg", "bg/classroom.png", ResourceType.Sprite, "png-data"u8.ToArray()));
        manager.RegisterProvider(provider);

        var result = await manager.LoadByPathAsync<byte[]>("bg/classroom.png");
        Assert.That(result, Is.Not.Null.And.Not.Empty);
        Assert.That(result, Is.EqualTo("png-data"u8.ToArray()));
    }

    [Test]
    public async Task LoadByPathAsync_UnknownPath_ReturnsNull()
    {
        using var manager = new AssetManager();
        manager.RegisterProvider(new MockProvider("test", new GameFile("id-1", "file.txt", ResourceType.Unknown, "data"u8.ToArray())));

        var result = await manager.LoadByPathAsync<byte[]>("nonexistent.png");
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task LoadByPathAsync_AndLoadById_ShareCache()
    {
        using var manager = new AssetManager();
        var provider = new MockProvider("test", new GameFile("id-bg", "bg/classroom.png", ResourceType.Sprite, "png-data"u8.ToArray()));
        manager.RegisterProvider(provider);

        // Load by path first
        var byPath = await manager.LoadByPathAsync<byte[]>("bg/classroom.png");
        Assert.That(provider.LoadCount, Is.EqualTo(1));

        // Load by ID — should hit cache, not provider
        var byId = await manager.LoadAsync<byte[]>("id-bg");
        Assert.That(byId, Is.SameAs(byPath));
        Assert.That(provider.LoadCount, Is.EqualTo(1));
    }

    [Test]
    public async Task LoadByPathAsync_AndLoadById_ShareRefCount()
    {
        using var manager = new AssetManager();
        var provider = new MockProvider("test", new GameFile("id-bg", "bg/classroom.png", ResourceType.Sprite, "png-data"u8.ToArray()));
        manager.RegisterProvider(provider);

        await manager.LoadByPathAsync<byte[]>("bg/classroom.png"); // ref=1
        await manager.LoadAsync<byte[]>("id-bg");                  // ref=2

        manager.Release("id-bg");                                  // ref=1
        Assert.That(manager.IsLoaded("id-bg"), Is.True);

        manager.Release("id-bg");                                  // ref=0
        Assert.That(manager.IsLoaded("id-bg"), Is.False);
    }

    [Test]
    public async Task LoadByPathAsync_PathNormalized_CaseInsensitive()
    {
        using var manager = new AssetManager();
        var provider = new MockProvider("test", new GameFile("id-bg", "bg/classroom.png", ResourceType.Sprite, "png-data"u8.ToArray()));
        manager.RegisterProvider(provider);

        var result = await manager.LoadByPathAsync<byte[]>("BG/Classroom.PNG");
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.EqualTo("png-data"u8.ToArray()));
    }

    [Test]
    public async Task LoadAsync_IdCaseDoesNotCreateAnotherCacheEntry()
    {
        using var manager = new AssetManager();
        var provider = new MockProvider("test", new GameFile("id-1", "file.txt", ResourceType.Unknown, "data"u8.ToArray()));
        manager.RegisterProvider(provider);

        var first = await manager.LoadAsync<byte[]>("id-1");
        var second = await manager.LoadAsync<byte[]>("ID-1");

        Assert.That(second, Is.SameAs(first));
        Assert.That(provider.LoadCount, Is.EqualTo(1));
        manager.Release<byte[]>("id-1");
        manager.Release<byte[]>("ID-1");
    }

    // ── Mock Provider ──

    private sealed class MockProvider(string name, IGameFile file) : IAssetProvider
    {
        public string Name { get; } = name;
        public int LoadCount;

        public bool Exists(string archiveName) => true;

        public IArchive OpenArchive(string archiveName) => new MockArchive(file);

        public Task<IArchive> OpenArchiveAsync(string archiveName, CancellationToken ct = default)
        {
            LoadCount++;
            return Task.FromResult<IArchive>(new MockArchive(file));
        }

        private sealed class MockArchive(IGameFile file) : IArchive
        {
            public string Name => "mock";
            public IEnumerable<string> AssetIds => [file.Id];

            public bool Contains(string assetId) => assetId == file.Id;
            public IGameFile? GetAsset(string assetId) => assetId == file.Id ? file : null;
            public IGameFile? GetAssetByPath(string path) =>
                string.Equals(path.Replace('\\', '/'), file.Path.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase) ? file : null;
            public void Dispose() { }
        }
    }

    private sealed class DisposableAsset : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private sealed class DisposableAssetDecoder : IAssetDecoder<DisposableAsset>
    {
        public int DecodeCount { get; private set; }

        public ValueTask<DisposableAsset?> DecodeAsync(IGameFile file, ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            DecodeCount++;
            return ValueTask.FromResult<DisposableAsset?>(new DisposableAsset());
        }
    }

    private sealed class BlockingAssetDecoder : IAssetDecoder<DisposableAsset>
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSignal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int DecodeCount { get; private set; }

        public async ValueTask<DisposableAsset?> DecodeAsync(IGameFile file, ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            DecodeCount++;
            Started.TrySetResult();
            await ReleaseSignal.Task.WaitAsync(ct);
            return new DisposableAsset();
        }

        public void Release() => ReleaseSignal.TrySetResult();
    }

    private sealed class NonCooperativeBlockingAssetDecoder : IAssetDecoder<DisposableAsset>
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSignal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DisposableAsset? Result { get; private set; }

        public async ValueTask<DisposableAsset?> DecodeAsync(IGameFile file, ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            Started.TrySetResult();
            await ReleaseSignal.Task;
            Result = new DisposableAsset();
            Completed.TrySetResult();
            return Result;
        }

        public void Release() => ReleaseSignal.TrySetResult();
    }
}
