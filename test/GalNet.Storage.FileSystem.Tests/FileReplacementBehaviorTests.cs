using GalNet.Core.Variable;
using GalNet.Storage.FileSystem;

namespace GeneralTest.Storage;

public sealed class FileReplacementBehaviorTests
{
    [Test]
    public async Task IndependentStoresDoNotShareTemporaryFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "galnet-file-replacement-" + Guid.NewGuid().ToString("N"));
        var stores = Enumerable.Range(0, 12).Select(_ => new FilePlayerVariableStore(root)).ToArray();
        try
        {
            await Task.WhenAll(stores.Select((store, index) => store.SaveAsync(new Dictionary<string, Variable>
            {
                ["value"] = Value(index)
            })));
            var loaded = await stores[0].LoadAsync();
            Assert.That(loaded["value"].AsInt(), Is.InRange(0, stores.Length - 1));
            Assert.That(Directory.GetFiles(root, "*.tmp"), Is.Empty);
        }
        finally
        {
            foreach (var store in stores) store.Dispose();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Test]
    public void ReplacementFailurePreservesDestinationAndCleansTemporaryFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "galnet-file-replacement-" + Guid.NewGuid().ToString("N"));
        var destination = Directory.CreateDirectory(Path.Combine(root, "player-variables.json")).FullName;
        File.WriteAllText(Path.Combine(destination, "sentinel"), "keep");
        try
        {
            using var store = new FilePlayerVariableStore(root);
            var error = Assert.CatchAsync<Exception>(() => store.SaveAsync(new Dictionary<string, Variable>()));
            Assert.That(error, Is.InstanceOf<IOException>().Or.InstanceOf<UnauthorizedAccessException>());
            Assert.That(File.ReadAllText(Path.Combine(destination, "sentinel")), Is.EqualTo("keep"));
            Assert.That(Directory.GetFiles(root, "*.tmp"), Is.Empty);
        }
        finally { Directory.Delete(root, true); }
    }

    private static Variable Value(int value)
    {
        var variable = new Variable { Name = "value" };
        variable.SetValue(value);
        return variable;
    }
}
