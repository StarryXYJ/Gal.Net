using GalNet.Assets.Provider;
using GalNet.Contracts.Builtins.Assets;
using GalNet.Core.Assets;

namespace GalNet.Assets.Tests;

public sealed class PakSnapshotTests
{
    private static readonly IResourceTypeCatalog Types = BuiltinResourceTypes.CreateCatalog();

    [TestCase(CompressionMode.None)]
    [TestCase(CompressionMode.Brotli)]
    public void Public_deserialize_keeps_a_snapshot_of_the_callers_buffer(CompressionMode compression)
    {
        var payload = CreatePayload();
        var pak = CreatePak(payload, compression);
        using var archive = Archive.Deserialize("assets", pak, Types);
        Array.Fill(pak, (byte)0);
        Assert.That(archive.GetAsset("data")!.ReadAllBytes(), Is.EqualTo(payload));
    }

    [TestCase(false, CompressionMode.None)]
    [TestCase(true, CompressionMode.None)]
    [TestCase(false, CompressionMode.Brotli)]
    [TestCase(true, CompressionMode.Brotli)]
    public async Task File_snapshot_survives_source_changes_but_next_open_rejects_them(bool asynchronous, CompressionMode compression)
    {
        var path = Path.Combine(Path.GetTempPath(), $"galnet-pak-snapshot-{Guid.NewGuid():N}.pak");
        var payload = CreatePayload();
        var pak = CreatePak(payload, compression);
        await File.WriteAllBytesAsync(path, pak);
        try
        {
            var provider = new PakFileProvider(path, "assets", Types, expectedSha256: CryptoHelper.HashSHA256(pak));
            using var archive = asynchronous ? await provider.OpenArchiveAsync("assets") : provider.OpenArchive("assets");
            payload[0] ^= 1;
            // Still a valid PAK: rejection must come from the frozen whole-package identity.
            await File.WriteAllBytesAsync(path, CreatePak(payload, compression));
            payload[0] ^= 1;
            Assert.That(archive.GetAsset("data")!.ReadAllBytes(), Is.EqualTo(payload));
            if (asynchronous)
                Assert.ThrowsAsync<InvalidDataException>(() => provider.OpenArchiveAsync("assets"));
            else
                Assert.Throws<InvalidDataException>(() => provider.OpenArchive("assets"));

            await File.WriteAllBytesAsync(path, pak);
            using var reopened = await provider.OpenArchiveAsync("assets");
            var reads = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => reopened.GetAsset("data")!.ReadAllBytes())));
            Assert.That(reads.All(bytes => bytes.AsSpan().SequenceEqual(payload)), Is.True);
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            Assert.Throws<OperationCanceledException>(() => reopened.GetAsset("data", canceled.Token));
        }
        finally { File.Delete(path); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Provider_owned_buffer_still_verifies_entry_hash(bool asynchronous)
    {
        var pak = CreatePak(CreatePayload(), CompressionMode.None);
        pak[^1] ^= 1;
        var provider = new PakStreamProvider(new Dictionary<string, Func<Stream>>
        {
            ["assets"] = () => new MemoryStream(pak, writable: false)
        }, Types);
        using var archive = asynchronous ? await provider.OpenArchiveAsync("assets") : provider.OpenArchive("assets");
        Assert.Throws<InvalidDataException>(() => archive.GetAsset("data"));
    }

    private static byte[] CreatePayload()
    {
        var bytes = new byte[4096];
        new Random(42).NextBytes(bytes);
        return bytes;
    }

    private static byte[] CreatePak(byte[] payload, CompressionMode compression)
    {
        var metadata = new AssetMeta { Id = "data", Path = "data.bin", TypeId = "data" };
        return Archive.Serialize("assets", [new GameFile(metadata.Id, metadata.Path, metadata.TypeId, metadata, payload)], Types, compression);
    }
}
