using GalNet.Core.Variable;
using GalNet.Storage.FileSystem;

namespace GalNet.Assets.Tests.Storage;

public sealed class FilePlayerVariableStoreTests
{
    [Test]
    public async Task SaveThenLoad_RestoresAnIndependentVariableSnapshot()
    {
        var directory = Path.Combine(Path.GetTempPath(), "GalNet.Storage.Tests", Path.GetRandomFileName());
        try
        {
            var source = new Variable { Name = "route" };
            source.SetValue("alpha");
            var store = new FilePlayerVariableStore(directory);

            await store.SaveAsync(new Dictionary<string, Variable> { ["route"] = source });
            source.SetValue("changed-after-save");
            var loaded = await store.LoadAsync();

            Assert.That(loaded["route"].AsString(), Is.EqualTo("alpha"));
            Assert.That(loaded["route"], Is.Not.SameAs(source));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
