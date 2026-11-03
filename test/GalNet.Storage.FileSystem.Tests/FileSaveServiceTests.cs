using System.Reflection;
using GalNet.Core.Runtime;
using GalNet.Runtime.Persistence;
using GalNet.Storage.FileSystem;

namespace GeneralTest.Storage;

public sealed class FileSaveServiceTests
{
    [Test]
    public async Task FailedCommitKeepsPreviousSaveAndPreview()
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("The test simulates Windows sharing denial.");
        var directory = CreateProfileDirectory();
        try
        {
            using var saves = new FileSaveService(directory);
            await saves.SaveAsync(0, new SaveRequest { Snapshot = new GameSnapshot { NodeId = "old" }, PreviewImage = [1, 2] });
            var before = await saves.GetSlotInfoAsync(0);
            var path = Path.Combine(directory, "saves", "slot-000.json");
            using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.That(Assert.CatchAsync(async () => await saves.SaveAsync(0, new SaveRequest { Snapshot = new GameSnapshot { NodeId = "new" }, PreviewImage = [3, 4] })),
                    Is.InstanceOf<IOException>().Or.InstanceOf<UnauthorizedAccessException>());
            var after = await saves.GetSlotInfoAsync(0);
            Assert.That((await saves.LoadAsync(0))!.NodeId, Is.EqualTo("old"));
            Assert.That(after.PreviewImage, Is.EqualTo(before.PreviewImage));
            Assert.That(await File.ReadAllBytesAsync(after.PreviewImage!), Is.EqualTo(new byte[] { 1, 2 }));
            Assert.That(Directory.GetFiles(Path.Combine(directory, "saves"), "slot-000-*.png"), Has.Length.EqualTo(1));
        }
        finally { DeleteProfileDirectory(directory); }
    }

    [Test]
    public async Task LegacyPreviewRemainsReadableAndOverwritePublishesNewPair()
    {
        var directory = CreateProfileDirectory();
        try
        {
            using var saves = new FileSaveService(directory);
            await saves.SaveAsync(0, new SaveRequest { Snapshot = new GameSnapshot { NodeId = "old" }, PreviewImage = [1] });
            var old = await saves.GetSlotInfoAsync(0);
            var path = Path.Combine(directory, "saves", "slot-000.json");
            var json = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
            json.Remove("PreviewFile");
            await File.WriteAllTextAsync(path, json.ToJsonString());
            File.Move(old.PreviewImage!, Path.Combine(directory, "saves", "slot-000.png"));
            Assert.That(await File.ReadAllBytesAsync((await saves.GetSlotInfoAsync(0)).PreviewImage!), Is.EqualTo(new byte[] { 1 }));
            await saves.SaveAsync(0, new SaveRequest { Snapshot = new GameSnapshot { NodeId = "new" }, PreviewImage = [2] });
            var current = await saves.GetSlotInfoAsync(0);
            Assert.That((await saves.LoadAsync(0))!.NodeId, Is.EqualTo("new"));
            Assert.That(await File.ReadAllBytesAsync(current.PreviewImage!), Is.EqualTo(new byte[] { 2 }));
            Assert.That(File.Exists(Path.Combine(directory, "saves", "slot-000.png")), Is.False);
            await saves.SaveAsync(0, new SaveRequest { Snapshot = new GameSnapshot { NodeId = "without-preview" } });
            Assert.That((await saves.GetSlotInfoAsync(0)).PreviewImage, Is.Null);
            Assert.That((await saves.LoadAsync(0))!.NodeId, Is.EqualTo("without-preview"));
        }
        finally { DeleteProfileDirectory(directory); }
    }
    [Test]
    public async Task SingleSlotLookupMatchesListMetadataForOccupiedEmptyAndCorruptSlots()
    {
        var directory = CreateProfileDirectory();
        try
        {
            using var saves = new FileSaveService(directory, 3);
            await saves.SaveAsync(0, new SaveRequest { Snapshot = new GameSnapshot { NodeId = "chapter" }, Description = "Name", PreviewImage = [1, 2] });
            await File.WriteAllTextAsync(Path.Combine(directory, "saves", "slot-002.json"), "broken");
            var all = await saves.ListSlotsAsync();
            for (var slot = 0; slot < 3; slot++)
            {
                var single = await saves.GetSlotInfoAsync(slot);
                Assert.That(single.Description, Is.EqualTo(all[slot].Description));
                Assert.That(single.Timestamp, Is.EqualTo(all[slot].Timestamp));
                Assert.That(single.PreviewImage, Is.EqualTo(all[slot].PreviewImage));
                Assert.That(single.IsCorrupt, Is.EqualTo(all[slot].IsCorrupt));
            }
            Assert.That(() => saves.GetSlotInfoAsync(3), Throws.TypeOf<ArgumentOutOfRangeException>());
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            Assert.That(async () => await saves.GetSlotInfoAsync(0, canceled.Token), Throws.InstanceOf<OperationCanceledException>());
        }
        finally { DeleteProfileDirectory(directory); }
    }

    [Test]
    public async Task RenamePersistsOnlyTheNameAndRejectsInvalidOrMissingSaves()
    {
        var directory = CreateProfileDirectory();
        try
        {
            using var saves = new FileSaveService(directory, 3);
            await saves.SaveAsync(0, new SaveRequest { Snapshot = new GameSnapshot { NodeId = "chapter-2" }, PreviewImage = [1, 2, 3] });
            var before = (await saves.ListSlotsAsync())[0];
            await saves.RenameAsync(0, "  雨中的重逢 / Reunion  ");
            using var reopened = new FileSaveService(directory, 3);
            var after = (await reopened.ListSlotsAsync())[0];
            Assert.Multiple(() =>
            {
                Assert.That(after.Description, Is.EqualTo("雨中的重逢 / Reunion"));
                Assert.That(after.Timestamp, Is.EqualTo(before.Timestamp));
                Assert.That(after.PreviewImage, Is.EqualTo(before.PreviewImage));
                Assert.That(File.ReadAllBytes(after.PreviewImage!), Is.EqualTo(new byte[] { 1, 2, 3 }));
            });
            Assert.That((await reopened.LoadAsync(0))!.NodeId, Is.EqualTo("chapter-2"));
            Assert.ThrowsAsync<ArgumentException>(() => saves.RenameAsync(0, new string('a', 121)));
            Assert.ThrowsAsync<ArgumentException>(() => saves.RenameAsync(0, "two\nlines"));
            Assert.ThrowsAsync<FileNotFoundException>(() => saves.RenameAsync(1, "missing"));
            var corrupt = Path.Combine(directory, "saves", "slot-002.json");
            await File.WriteAllTextAsync(corrupt, "broken");
            Assert.CatchAsync(() => saves.RenameAsync(2, "invalid"));
            Assert.That(await File.ReadAllTextAsync(corrupt), Is.EqualTo("broken"));
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            Assert.That(async () => await saves.RenameAsync(0, "canceled", canceled.Token), Throws.InstanceOf<OperationCanceledException>());
            Assert.That((await saves.ListSlotsAsync())[0].Description, Is.EqualTo("雨中的重逢 / Reunion"));
            await saves.RenameAsync(0, "  ");
            Assert.That((await saves.ListSlotsAsync())[0].Description, Is.Null);
        }
        finally { DeleteProfileDirectory(directory); }
    }

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
