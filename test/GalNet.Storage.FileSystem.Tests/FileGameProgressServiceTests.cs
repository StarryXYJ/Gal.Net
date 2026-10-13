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
    public async Task StableKeysPersistWithoutSlashCollisions()
    {
        await using var progress = new FileGameProgressService(_directory, TimeSpan.FromDays(1));
        progress.MarkRead("chapter/a", "line");
        Assert.That(File.Exists(Path.Combine(_directory, "progress.json")), Is.False);
        await progress.FlushAsync();
        await using var restored = new FileGameProgressService(_directory);
        Assert.Multiple(() =>
        {
            Assert.That(restored.IsRead("chapter/a", "line"), Is.True);
            Assert.That(restored.IsRead("chapter", "a/line"), Is.False);
        });
    }

    [Test]
    public async Task FailedFlushKeepsTheDirtyIndexAndCanBeRetried()
    {
        var profile = Path.Combine(_directory, "profile");
        File.WriteAllText(profile, "obstruct directory creation");
        await using var progress = new FileGameProgressService(profile, TimeSpan.FromDays(1));
        progress.MarkRead("chapter", "line");
        Assert.That(() => progress.FlushAsync(), Throws.InstanceOf<IOException>());
        Assert.That(progress.LastPersistenceError, Is.Not.Null);
        Assert.That(progress.IsRead("chapter", "line"), Is.True);
        File.Delete(profile);
        await progress.FlushAsync();
        await using var restored = new FileGameProgressService(profile);
        Assert.That(restored.IsRead("chapter", "line"), Is.True);
        Assert.That(progress.LastPersistenceError, Is.Null);
    }

    [Test]
    public async Task ClearAndCanceledFlushDoNotResurrectBufferedProgress()
    {
        await using var progress = new FileGameProgressService(_directory, TimeSpan.FromDays(1));
        progress.MarkRead("chapter", "line");
        Assert.That(() => progress.FlushAsync(new CancellationToken(true)), Throws.InstanceOf<OperationCanceledException>());
        progress.Clear();
        await progress.FlushAsync();
        Assert.That(progress.IsRead("chapter", "line"), Is.False);
        Assert.That(File.Exists(Path.Combine(_directory, "progress.json")), Is.False);
    }

    [Test]
    public async Task PeriodicPersistenceCommitsWithoutBlockingMarkRead()
    {
        await using var progress = new FileGameProgressService(_directory, TimeSpan.FromMilliseconds(20));
        progress.MarkRead("chapter", "line");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!File.Exists(Path.Combine(_directory, "progress.json"))) await Task.Delay(10, timeout.Token);
        await using var restored = new FileGameProgressService(_directory);
        Assert.That(restored.IsRead("chapter", "line"), Is.True);
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
