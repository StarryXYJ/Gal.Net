using GalNet.Core.Assets;

namespace GalNet.Assets.Tests;

public sealed class ArchiveTests
{
    private static readonly IGameFile[] TestFiles =
    [
        new GameFile("id-bg-classroom", "bg/classroom.png", ResourceType.Sprite,
            "fake-png-data-for-classroom"u8.ToArray()),
        new GameFile("id-bgm-main", "audio/bgm/main.ogg", ResourceType.Audio,
            "fake-ogg-data"u8.ToArray()),
        new GameFile("id-char-alice", "characters/alice.png", ResourceType.Sprite,
            "fake-png-data-for-alice"u8.ToArray(), "abc123hash"),
    ];

    [Test]
    public void Serialize_Deserialize_Roundtrip_ReturnsSameData()
    {
        var pak = Archive.Serialize("test", TestFiles);
        var archive = Archive.Deserialize("test", pak);

        Assert.That(archive.Name, Is.EqualTo("test"));
        Assert.That(archive.AssetIds, Is.EquivalentTo(TestFiles.Select(f => f.Id)));
    }

    [Test]
    public void GetAsset_ById_ReturnsCorrectFile()
    {
        var pak = Archive.Serialize("test", TestFiles);
        using var archive = Archive.Deserialize("test", pak);

        var file = archive.GetAsset("id-bg-classroom");
        Assert.That(file, Is.Not.Null);
        Assert.That(file!.Id, Is.EqualTo("id-bg-classroom"));
        Assert.That(file.Path, Is.EqualTo("bg/classroom.png"));
        Assert.That(file.Type, Is.EqualTo(ResourceType.Sprite));
        Assert.That(file.ReadAllBytes(), Is.EqualTo(TestFiles[0].ReadAllBytes()));
    }

    [Test]
    public void GetAsset_ByPath_ReturnsCorrectFile()
    {
        var pak = Archive.Serialize("test", TestFiles);
        using var archive = Archive.Deserialize("test", pak);

        var file = archive.GetAssetByPath("audio/bgm/main.ogg");
        Assert.That(file, Is.Not.Null);
        Assert.That(file!.Id, Is.EqualTo("id-bgm-main"));
    }

    [Test]
    public void GetAsset_ByPath_CaseInsensitive()
    {
        var pak = Archive.Serialize("test", TestFiles);
        using var archive = Archive.Deserialize("test", pak);

        var file = archive.GetAssetByPath("AUDIO/BGM/MAIN.OGG");
        Assert.That(file, Is.Not.Null);
        Assert.That(file!.Id, Is.EqualTo("id-bgm-main"));
    }

    [Test]
    public void GetAsset_NotFound_ReturnsNull()
    {
        var pak = Archive.Serialize("test", TestFiles);
        using var archive = Archive.Deserialize("test", pak);

        Assert.That(archive.GetAsset("nonexistent"), Is.Null);
        Assert.That(archive.GetAssetByPath("nonexistent.png"), Is.Null);
    }

    [Test]
    public void Contains_ReturnsCorrect()
    {
        var pak = Archive.Serialize("test", TestFiles);
        using var archive = Archive.Deserialize("test", pak);

        Assert.That(archive.Contains("id-bg-classroom"), Is.True);
        Assert.That(archive.Contains("nonexistent"), Is.False);
    }

    [Test]
    public void Serialize_WithCompression_ReducesSize()
    {
        var largeFiles = new IGameFile[]
        {
            new GameFile("id-large", "data/large.bin", ResourceType.Unknown,
                new byte[10000]),
        };

        var pakCompressed = Archive.Serialize("test", largeFiles, CompressionMode.Brotli);
        var pakNone = Archive.Serialize("test", largeFiles, CompressionMode.None);

        Assert.That(pakCompressed.Length, Is.LessThan(pakNone.Length));
    }

    [Test]
    public void Serialize_WithoutCompression_AllFilesAccessible()
    {
        var pak = Archive.Serialize("test", TestFiles, CompressionMode.None);
        using var archive = Archive.Deserialize("test", pak);

        foreach (var original in TestFiles)
        {
            var loaded = archive.GetAsset(original.Id);
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.ReadAllBytes(), Is.EqualTo(original.ReadAllBytes()));
        }
    }

    [Test]
    public void Serialize_PreservesHash()
    {
        var pak = Archive.Serialize("test", TestFiles);
        using var archive = Archive.Deserialize("test", pak);

        var file = archive.GetAsset("id-char-alice");
        Assert.That(file, Is.Not.Null);
        Assert.That(file!.Hash, Is.EqualTo("abc123hash"));
    }

    [Test]
    public void Dispose_MakesGetAssetThrow()
    {
        var pak = Archive.Serialize("test", TestFiles);
        var archive = Archive.Deserialize("test", pak);
        archive.Dispose();

        Assert.That(() => archive.GetAsset("id-bg-classroom"), Throws.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public void Deserialize_InvalidMagic_Throws()
    {
        var invalid = "NOTGPAK"u8.ToArray();
        Assert.That(() => Archive.Deserialize("bad", invalid), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void AutoHash_WhenHashMissing_ComputesOnSerialize()
    {
        var files = new IGameFile[]
        {
            new GameFile("id-no-hash", "data/file.bin", ResourceType.Unknown,
                "some-data"u8.ToArray()), // No hash provided
        };

        var pak = Archive.Serialize("test", files);
        using var archive = Archive.Deserialize("test", pak);

        var file = archive.GetAsset("id-no-hash");
        Assert.That(file, Is.Not.Null);
        Assert.That(file!.Hash, Is.Not.Null.And.Not.Empty);
        Assert.That(file.Hash, Has.Length.EqualTo(64));
    }
}
