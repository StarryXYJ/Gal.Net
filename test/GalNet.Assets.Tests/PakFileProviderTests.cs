using GalNet.Assets.Provider;
using GalNet.Core.Assets;

namespace GalNet.Assets.Tests;

public sealed class PakFileProviderTests
{
    private string _tempDir = null!;
    private string _pakPath = null!;
    private const string ArchiveName = "test-assets";

    [SetUp]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        // Build a .pak file
        var files = new IGameFile[]
        {
            new GameFile("id-bg", "bg/classroom.png", ResourceType.Sprite, "png-data-123"u8.ToArray()),
            new GameFile("id-bgm", "audio/bgm.ogg", ResourceType.Audio, new byte[5000]),
            new GameFile("id-voice", "audio/voice/intro.ogg", ResourceType.Audio, "voice-data"u8.ToArray()),
        };
        _pakPath = Path.Combine(_tempDir, $"{ArchiveName}.pak");
        PakBuilder.BuildToFile(_pakPath, files, CompressionMode.Brotli);
    }

    [TearDown]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Test]
    public void OpenArchive_ReturnsArchive()
    {
        var provider = new PakFileProvider(_tempDir);
        using var archive = provider.OpenArchive(ArchiveName);

        Assert.That(archive, Is.Not.Null);
        Assert.That(archive.Name, Is.EqualTo(ArchiveName));
        Assert.That(archive.AssetIds, Is.EquivalentTo(["id-bg", "id-bgm", "id-voice"]));
    }

    [Test]
    public async Task OpenArchiveAsync_ReturnsArchive()
    {
        var provider = new PakFileProvider(_tempDir);
        using var archive = await provider.OpenArchiveAsync(ArchiveName);

        Assert.That(archive, Is.Not.Null);
        Assert.That(archive.AssetIds.ToArray(), Has.Length.EqualTo(3));
    }

    [Test]
    public void GetAsset_ById_ReturnsCorrectData()
    {
        var provider = new PakFileProvider(_tempDir);
        using var archive = provider.OpenArchive(ArchiveName);

        var file = archive.GetAsset("id-bg");
        Assert.That(file, Is.Not.Null);
        Assert.That(file!.Id, Is.EqualTo("id-bg"));
        Assert.That(file.Path, Is.EqualTo("bg/classroom.png"));
        Assert.That(file.Type, Is.EqualTo(ResourceType.Sprite));
        Assert.That(file.ReadAllBytes(), Is.EqualTo("png-data-123"u8.ToArray()));
    }

    [Test]
    public void GetAsset_ByPath_Works()
    {
        var provider = new PakFileProvider(_tempDir);
        using var archive = provider.OpenArchive(ArchiveName);

        var file = archive.GetAssetByPath("audio/voice/intro.ogg");
        Assert.That(file, Is.Not.Null);
        Assert.That(file!.Id, Is.EqualTo("id-voice"));
    }

    [Test]
    public void GetAsset_NotFound_ReturnsNull()
    {
        var provider = new PakFileProvider(_tempDir);
        using var archive = provider.OpenArchive(ArchiveName);

        Assert.That(archive.GetAsset("nonexistent"), Is.Null);
    }

    [Test]
    public void Exists_WithPakFile_ReturnsTrue()
    {
        var provider = new PakFileProvider(_tempDir);
        Assert.That(provider.Exists(ArchiveName), Is.True);
    }

    [Test]
    public void Exists_WithoutPakFile_ReturnsFalse()
    {
        var provider = new PakFileProvider(_tempDir);
        Assert.That(provider.Exists("nonexistent"), Is.False);
    }

    [Test]
    public void OpenArchive_MissingFile_WithOptional_ReturnsEmpty()
    {
        var provider = new PakFileProvider(_tempDir, optional: true);
        using var archive = provider.OpenArchive("nonexistent");

        Assert.That(archive.AssetIds, Is.Empty);
        Assert.That(archive.GetAsset("anything"), Is.Null);
    }

    [Test]
    public void OpenArchive_MissingFile_Throws()
    {
        var provider = new PakFileProvider(_tempDir);
        Assert.That(() => provider.OpenArchive("nonexistent"), Throws.TypeOf<FileNotFoundException>());
    }

    [Test]
    public void LargeAsset_Roundtrip_Works()
    {
        var provider = new PakFileProvider(_tempDir);
        using var archive = provider.OpenArchive(ArchiveName);

        var file = archive.GetAsset("id-bgm");
        Assert.That(file, Is.Not.Null);
        Assert.That(file!.Length, Is.EqualTo(5000));
    }
}
