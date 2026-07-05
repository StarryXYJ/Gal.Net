using GalNet.Core.Assets;

namespace GalNet.Assets.Tests;

public sealed class PakBuilderTests
{
    [Test]
    public void Build_BasicRoundtrip()
    {
        var files = new IGameFile[]
        {
            new GameFile("id-1", "file1.txt", ResourceType.Unknown, "content-1"u8.ToArray()),
            new GameFile("id-2", "file2.txt", ResourceType.Unknown, "content-2"u8.ToArray()),
        };

        var pak = PakBuilder.Build("test", files);
        Assert.That(pak, Is.Not.Null.And.Not.Empty);

        using var archive = Archive.Deserialize("test", pak);
        Assert.That(archive.AssetIds.ToArray(), Has.Length.EqualTo(2));
    }

    [Test]
    public void BuildToFile_WritesValidPak()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pak");
        try
        {
            var files = new IGameFile[]
            {
                new GameFile("id-test", "test.bin", ResourceType.Unknown, "data"u8.ToArray()),
            };

            PakBuilder.BuildToFile(tempFile, files);
            Assert.That(File.Exists(tempFile), Is.True);

            var readData = File.ReadAllBytes(tempFile);
            using var archive = Archive.Deserialize("test", readData);
            Assert.That(archive.GetAsset("id-test"), Is.Not.Null);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Test]
    public void Build_EmptyFileList_ProducesValidArchive()
    {
        var pak = PakBuilder.Build("empty", []);
        using var archive = Archive.Deserialize("empty", pak);

        Assert.That(archive.AssetIds, Is.Empty);
        Assert.That(archive.Name, Is.EqualTo("empty"));
    }

    [Test]
    public void Build_WithDifferentCompression_Roundtrips(
        [Values]CompressionMode mode)
    {
        var files = new IGameFile[]
        {
            new GameFile("id-data", "data.bin", ResourceType.Unknown, new byte[1000]),
        };

        var pak = PakBuilder.Build("test", files, mode);
        using var archive = Archive.Deserialize("test", pak);

        var loaded = archive.GetAsset("id-data");
        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.Length, Is.EqualTo(1000));
    }
}
