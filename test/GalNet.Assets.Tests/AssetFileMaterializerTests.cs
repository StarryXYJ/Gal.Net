using GalNet.Assets.Provider;
using GalNet.Assets;

namespace GeneralTest.Assets;

public sealed class AssetFileMaterializerTests
{
    [Test]
    public async Task ConcurrentConsumersShareFileUntilTheLastLeaseAndSurviveOwnerDisposal()
    {
        var root = Path.Combine(Path.GetTempPath(), $"galnet-materializer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(root, "clip.mp4"), [1, 2, 3]);
            await File.WriteAllTextAsync(Path.Combine(root, "clip.mp4.meta"), """{"id":"clip","type":"video","path":"clip.mp4","compress":"none"}""");
            using var assets = new AssetManager([new LocalFileProvider(root)]);
            await using var files = new AssetFileMaterializer(assets);
            var leases = await Task.WhenAll(files.AcquireAsync("clip"), files.AcquireAsync("CLIP"));
            try
            {
                Assert.That(leases[0].Path, Is.EqualTo(leases[1].Path));
                Assert.That(await File.ReadAllBytesAsync(leases[0].Path), Is.EqualTo(new byte[] { 1, 2, 3 }));
                leases[0].Dispose();
                await files.DisposeAsync();
                Assert.That(File.Exists(leases[1].Path), Is.True, "A borrowed file must outlive materializer disposal.");
                Assert.ThrowsAsync<ObjectDisposedException>(() => files.AcquireAsync("clip"));
            }
            finally { foreach (var lease in leases) lease.Dispose(); }
            Assert.That(File.Exists(leases[1].Path), Is.False);
            Assert.That(Directory.Exists(Path.GetDirectoryName(leases[1].Path)), Is.False);
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public async Task CancellationDoesNotPoisonTheNextAcquire()
    {
        var root = Path.Combine(Path.GetTempPath(), $"galnet-materializer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(root, "clip.mp4"), [1]);
            await File.WriteAllTextAsync(Path.Combine(root, "clip.mp4.meta"), """{"id":"clip","type":"video","path":"clip.mp4","compress":"none"}""");
            using var assets = new AssetManager([new LocalFileProvider(root)]);
            await using var files = new AssetFileMaterializer(assets);
            Assert.That(() => files.AcquireAsync("clip", new CancellationToken(true)), Throws.InstanceOf<OperationCanceledException>());
            using var lease = await files.AcquireAsync("clip");
            Assert.That(File.Exists(lease.Path), Is.True);
        }
        finally { Directory.Delete(root, true); }
    }
}
