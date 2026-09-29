using System.Reflection;
using GalNet.Core.Runtime;
using GalNet.Runtime.Persistence;
using GalNet.Storage.FileSystem;

namespace GeneralTest.Storage;

public sealed class FileSaveServiceTests
{
    [Test]
    public async Task SaveRequest_RoundTripsSnapshotMetadataAndPreview()
    {
        var profileDirectory = CreateProfileDirectory();
        try
        {
            var saves = new FileSaveService(profileDirectory, maxSlots: 2);
            await saves.SaveAsync(0, new SaveRequest
            {
                Snapshot = new GameSnapshot { NodeId = "chapter-2" },
                Description = "After the choice",
                PreviewImage = [1, 2, 3, 4]
            });

            var loaded = await saves.LoadAsync(0);
            var info = (await saves.ListSlotsAsync())[0];

            Assert.Multiple(() =>
            {
                Assert.That(loaded?.NodeId, Is.EqualTo("chapter-2"));
                Assert.That(info.Description, Is.EqualTo("After the choice"));
                Assert.That(info.IsCorrupt, Is.False);
                Assert.That(info.PreviewImage, Is.Not.Null);
                Assert.That(File.ReadAllBytes(info.PreviewImage!), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
            });
        }
        finally { DeleteProfileDirectory(profileDirectory); }
    }

    [Test]
    public async Task QuickSaveRequest_RoundTripsAndCanBeDeleted()
    {
        var profileDirectory = CreateProfileDirectory();
        try
        {
            var saves = new FileSaveService(profileDirectory);
            await saves.QuickSaveAsync(new SaveRequest
            {
                Snapshot = new GameSnapshot { NodeId = "quick" },
                Description = "Quick save"
            });

            Assert.That((await saves.QuickLoadAsync())?.NodeId, Is.EqualTo("quick"));
            Assert.That((await saves.GetQuickSaveInfoAsync())?.Description, Is.EqualTo("Quick save"));
            Assert.That(await saves.HasQuickSaveAsync(), Is.True);

            await saves.DeleteQuickSaveAsync();

            Assert.That(await saves.HasQuickSaveAsync(), Is.False);
        }
        finally { DeleteProfileDirectory(profileDirectory); }
    }

    [Test]
    public void SaveAsync_RejectsPreCanceledOperation()
    {
        var profileDirectory = CreateProfileDirectory();
        try
        {
            var saves = new FileSaveService(profileDirectory);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.That(
                async () => await saves.SaveAsync(0, new SaveRequest { Snapshot = new GameSnapshot() }, cancellation.Token),
                Throws.InstanceOf<OperationCanceledException>());
        }
        finally { DeleteProfileDirectory(profileDirectory); }
    }

    [Test]
    public async Task LoadAsync_PropagatesCancellation()
    {
        var profileDirectory = CreateProfileDirectory();
        try
        {
            var saves = new FileSaveService(profileDirectory);
            await saves.SaveAsync(0, new SaveRequest { Snapshot = new GameSnapshot { NodeId = "saved" } });
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.That(
                async () => await saves.LoadAsync(0, cancellation.Token),
                Throws.InstanceOf<OperationCanceledException>());
        }
        finally { DeleteProfileDirectory(profileDirectory); }
    }

    [Test]
    public void ISaveService_ExposesOnlyCancelableAsyncIoAndSaveRequests()
    {
        var methods = typeof(ISaveService).GetMethods().Where(method => typeof(Task).IsAssignableFrom(method.ReturnType)).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(methods, Is.Not.Empty);
            Assert.That(methods.SelectMany(method => method.GetParameters()), Has.None.Matches<ParameterInfo>(parameter => parameter.ParameterType == typeof(GameSnapshot)));
            Assert.That(methods, Has.All.Matches<MethodInfo>(method => method.GetParameters().LastOrDefault()?.ParameterType == typeof(CancellationToken)));
        });
    }

    [Test]
    public async Task ClearAsync_removes_all_normal_and_quick_saves()
    {
        var profileDirectory = CreateProfileDirectory();
        try
        {
            var saves = new FileSaveService(profileDirectory, maxSlots: 2);
            await saves.SaveAsync(0, new SaveRequest { Snapshot = new GameSnapshot { NodeId = "first" } });
            await saves.SaveAsync(1, new SaveRequest { Snapshot = new GameSnapshot { NodeId = "second" } });
            await saves.QuickSaveAsync(new SaveRequest { Snapshot = new GameSnapshot { NodeId = "quick" } });

            await saves.ClearAsync();

            Assert.That(await saves.LoadAsync(0), Is.Null);
            Assert.That(await saves.LoadAsync(1), Is.Null);
            Assert.That(await saves.QuickLoadAsync(), Is.Null);
        }
        finally
        {
            DeleteProfileDirectory(profileDirectory);
        }
    }

    private static string CreateProfileDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "GalNet.Storage.Tests", Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteProfileDirectory(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }
}
