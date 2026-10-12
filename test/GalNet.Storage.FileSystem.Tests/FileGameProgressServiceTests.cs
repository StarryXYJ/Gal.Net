using GalNet.Storage.FileSystem;

namespace GeneralTest.Storage;

public class FileGameProgressServiceTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp() => _directory = Directory.CreateTempSubdirectory("galnet-progress-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [Test]
    public void StableKeysPersistWithoutSlashCollisions()
    {
        var progress = new FileGameProgressService(_directory);
        progress.MarkRead("chapter/a", "line");
        progress = new FileGameProgressService(_directory);
        Assert.Multiple(() =>
        {
            Assert.That(progress.IsRead("chapter/a", "line"), Is.True);
            Assert.That(progress.IsRead("chapter", "a/line"), Is.False);
        });
    }

    [Test]
    public void PreviousFormatIsRejectedWithoutChangingPlayerData()
    {
        var path = Path.Combine(_directory, "progress.json");
        const string oldData = "{\"ReadEntries\":[\"chapter/1\"]}";
        File.WriteAllText(path, oldData);
        Assert.Throws<InvalidDataException>(() => new FileGameProgressService(_directory));
        Assert.That(File.ReadAllText(path), Is.EqualTo(oldData));
        Assert.That(Directory.GetFiles(_directory), Has.Length.EqualTo(1));
    }
}
