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
}
