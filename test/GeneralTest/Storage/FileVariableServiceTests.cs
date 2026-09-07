using GalNet.Core.Variable;
using GalNet.Storage.FileSystem;
using GalVariable = GalNet.Core.Variable.Variable;

namespace GeneralTest.Storage;

public sealed class FileVariableServiceTests
{
    private string _profileDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        _profileDirectory = Path.Combine(Path.GetTempPath(), "GalNet.Storage.Tests", Path.GetRandomFileName());
        Directory.CreateDirectory(_profileDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_profileDirectory))
            Directory.Delete(_profileDirectory, true);
    }

    [Test]
    public async Task FlushPlayerVariablesAsync_PersistsOnlyPlayerScope()
    {
        var service = await FileVariableService.CreateAsync(new FilePlayerVariableStore(_profileDirectory));
        var player = new GalVariable { Name = "score" };
        player.SetValue(42);
        var save = new GalVariable { Name = "health" };
        save.SetValue(7);

        service.NotifyVariableChanged(VariableScope.Player, "score", player);
        service.NotifyVariableChanged(VariableScope.Save, "health", save);
        await service.FlushPlayerVariablesAsync();

        var reloaded = await FileVariableService.CreateAsync(new FilePlayerVariableStore(_profileDirectory));

        Assert.That(reloaded.GetSnapshot(VariableScope.Player)["score"].AsInt(), Is.EqualTo(42));
        Assert.That(reloaded.GetSnapshot(VariableScope.Save), Is.Empty);
    }
}
