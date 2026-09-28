using GalNet.Core.Runtime;
using GalNet.Storage.FileSystem;

namespace GeneralTest.Storage;

public sealed class FileSaveServiceTests
{
    [Test]
    public async Task ClearAsync_removes_all_normal_and_quick_saves()
    {
        var profileDirectory = Path.Combine(Path.GetTempPath(), "GalNet.Storage.Tests", Path.GetRandomFileName());
        try
        {
            var saves = new FileSaveService(profileDirectory, maxSlots: 2);
            await saves.SaveAsync(0, new GameSnapshot { NodeId = "first" });
            await saves.SaveAsync(1, new GameSnapshot { NodeId = "second" });
            await saves.QuickSaveAsync(new GameSnapshot { NodeId = "quick" });

            await saves.ClearAsync();

            Assert.That(await saves.LoadAsync(0), Is.Null);
            Assert.That(await saves.LoadAsync(1), Is.Null);
            Assert.That(await saves.QuickLoadAsync(), Is.Null);
        }
        finally
        {
            if (Directory.Exists(profileDirectory))
                Directory.Delete(profileDirectory, true);
        }
    }
}
