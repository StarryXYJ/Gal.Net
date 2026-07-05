using System.Text.Json;
using GalNet.Assets.Provider;
using GalNet.Core.Assets;

namespace GalNet.Assets.Tests;

public sealed class LocalFileProviderTests
{
    private string _tempDir = null!;
    private LocalFileProvider _provider = null!;

    [SetUp]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _provider = new LocalFileProvider(_tempDir);
    }

    [TearDown]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private void CreateAsset(string relativePath, string id, ResourceType type, byte[] data, string? compress = null)
    {
        // Create directory
        var fullPath = Path.Combine(_tempDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var dir = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(dir);

        // Write resource file
        File.WriteAllBytes(fullPath, data);

        // Write .meta file
        var meta = new AssetMeta
        {
            Id = id,
            Type = type.ToString().ToLower(),
            Path = relativePath.Replace('\\', '/'),
            Compress = compress ?? "none",
        };
        File.WriteAllText(fullPath + ".meta", JsonSerializer.Serialize(meta));
    }

    [Test]
    public async Task OpenArchiveAsync_WithMetaFiles_LoadsAllAssets()
    {
        CreateAsset("bg/classroom.png", "id-bg-1", ResourceType.Sprite, "png-data"u8.ToArray());
        CreateAsset("audio/bgm.ogg", "id-bgm-1", ResourceType.Audio, "ogg-data"u8.ToArray());

        using var archive = await _provider.OpenArchiveAsync("assets");

        Assert.That(archive.AssetIds.ToArray(), Has.Length.EqualTo(2));
        Assert.That(archive.Contains("id-bg-1"), Is.True);
        Assert.That(archive.Contains("id-bgm-1"), Is.True);
    }

    [Test]
    public async Task GetAsset_ByPath_Works()
    {
        CreateAsset("characters/alice.png", "id-alice", ResourceType.Sprite, "alice-png"u8.ToArray());

        using var archive = await _provider.OpenArchiveAsync("assets");

        var file = archive.GetAssetByPath("characters/alice.png");
        Assert.That(file, Is.Not.Null);
        Assert.That(file!.Id, Is.EqualTo("id-alice"));
        Assert.That(file.Type, Is.EqualTo(ResourceType.Sprite));
        Assert.That(file.ReadAllBytes(), Is.EqualTo("alice-png"u8.ToArray()));
    }

    [Test]
    public async Task GetAsset_ById_ReturnsCorrectFile()
    {
        CreateAsset("data/file.bin", "id-data-1", ResourceType.Unknown, "binary-data"u8.ToArray());

        using var archive = await _provider.OpenArchiveAsync("assets");

        var file = archive.GetAsset("id-data-1");
        Assert.That(file, Is.Not.Null);
        Assert.That(file!.Path, Is.EqualTo("data/file.bin"));
    }

    [Test]
    public async Task Assets_HaveCorrectHash()
    {
        var data = "hello-world"u8.ToArray();
        CreateAsset("test.txt", "id-test", ResourceType.Unknown, data);

        using var archive = await _provider.OpenArchiveAsync("assets");
        var file = archive.GetAsset("id-test");

        Assert.That(file, Is.Not.Null);
        Assert.That(file!.Hash, Is.EqualTo(CryptoHelper.HashSHA256(data)));
    }

    [Test]
    public void Exists_WithDirectory_ReturnsTrue()
    {
        Assert.That(_provider.Exists("assets"), Is.True);
    }

    [Test]
    public void Exists_WithoutDirectory_ReturnsFalse()
    {
        var p = new LocalFileProvider(Path.Combine(Path.GetTempPath(), "nonexistent-" + Guid.NewGuid()));
        Assert.That(p.Exists("assets"), Is.False);
    }

    [Test]
    public async Task OpenArchiveAsync_MissingDirectory_WithOptional_ReturnsEmpty()
    {
        var p = new LocalFileProvider(Path.Combine(Path.GetTempPath(), "nonexistent-" + Guid.NewGuid()), optional: true);
        using var archive = await p.OpenArchiveAsync("assets");
        Assert.That(archive.AssetIds, Is.Empty);
    }
}
