using GalNet.Core.Assets;
using GalNet.Core.Graph;
using GalNet.Core.Runtime;
using GalNet.Core.Variable;
using GalNet.GameHost.Content;
using GalNet.Runtime.Persistence;
using GalNet.Storage.FileSystem;
using GalVariable = GalNet.Core.Variable.Variable;

namespace GalNet.IntegrationTests;

public sealed class SampleSaveSessionTests
{
    [Test]
    public async Task CustomSlotNameSurvivesOverwriteAndReopeningThePlayer()
    {
        var directory = Path.Combine(Path.GetTempPath(), "galnet-save-name-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var player = await FilePlayerSession.OpenAsync(directory, new GameContent { Graph = new Graph() }.Gallery, maxSlots: 1))
            {
                await player.SaveAsync(0, new GameSnapshot { NodeId = "before" }, CancellationToken.None);
                await player.RenameSlotAsync(0, "再次相遇");
                await player.SaveAsync(0, new GameSnapshot { NodeId = "after" }, CancellationToken.None);
            }
            using var reopened = await FilePlayerSession.OpenAsync(directory, new GameContent { Graph = new Graph() }.Gallery, maxSlots: 1);
            Assert.That((await reopened.ListSlotsAsync(CancellationToken.None))[0].Description, Is.EqualTo("再次相遇"));
            Assert.That((await reopened.LoadAsync(0, CancellationToken.None))!.NodeId, Is.EqualTo("after"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Test]
    public async Task SavePreviewIsStoredAndClearedOnOverwriteWithoutPreview()
    {
        var directory = Path.Combine(Path.GetTempPath(), "galnet-preview-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var session = await FilePlayerSession.OpenAsync(directory, new GameContent { Graph = new Graph() }.Gallery,
                maxSlots: 1, cancellationToken: CancellationToken.None);
            byte[] preview = [1, 2, 3];
            await session.SaveAsync(0, new GameSnapshot(), preview, CancellationToken.None);
            var path = (await session.ListSlotsAsync(CancellationToken.None))[0].PreviewImage;
            Assert.That(path, Is.Not.Null);
            Assert.That(await File.ReadAllBytesAsync(path!), Is.EqualTo(preview));
            await session.SaveAsync(0, new GameSnapshot(), CancellationToken.None);
            Assert.That((await session.ListSlotsAsync(CancellationToken.None))[0].PreviewImage, Is.Null);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Test]
    public async Task FirstEmptySavesSerializeAndNeverOverwriteOccupiedOrCorruptSlots()
    {
        var directory = Path.Combine(Path.GetTempPath(), "galnet-empty-slot-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var assets = new EmptyAssetManager();
            using var session = await FilePlayerSession.OpenAsync(directory, new GameContent { Graph = new Graph() }.Gallery,
                cancellationToken: CancellationToken.None);
            await session.SaveAsync(0, new GameSnapshot { NodeId = "original" }, CancellationToken.None);
            await File.WriteAllTextAsync(Path.Combine(directory, "saves", "slot-001.json"), "corrupt");
            var slots = await Task.WhenAll(
                session.SaveFirstEmptyAsync(new GameSnapshot { NodeId = "first" }, CancellationToken.None),
                session.SaveFirstEmptyAsync(new GameSnapshot { NodeId = "second" }, CancellationToken.None));
            Assert.That(slots, Is.EqualTo(new int?[] { 2, 3 }));
            Assert.That((await session.LoadAsync(0, CancellationToken.None))!.NodeId, Is.EqualTo("original"));
            Assert.That(await File.ReadAllTextAsync(Path.Combine(directory, "saves", "slot-001.json")), Is.EqualTo("corrupt"));
            for (var index = 4; index < 60; index++)
                await session.SaveAsync(index, new GameSnapshot(), CancellationToken.None);
            Assert.That(await session.SaveFirstEmptyAsync(new GameSnapshot(), CancellationToken.None), Is.Null);
            Assert.That((await session.LoadAsync(0, CancellationToken.None))!.NodeId, Is.EqualTo("original"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Test]
    public async Task ClearAsync_resets_all_player_owned_state()
    {
        var profileDirectory = Path.Combine(
            Path.GetTempPath(),
            "GalNet.IntegrationTests",
            Path.GetRandomFileName());
        Directory.CreateDirectory(profileDirectory);
        try
        {
            using var assets = new EmptyAssetManager();
            using var session = await FilePlayerSession.OpenAsync(profileDirectory, new GameContent { Graph = new Graph() }.Gallery,
                maxSlots: 3, cancellationToken: CancellationToken.None);
            var score = new GalVariable { Name = "score" };
            score.SetValue(42);
            session.Variables.NotifyVariableChanged(VariableScope.Player, score.Name, score);
            session.Progress.MarkRead("chapter-1", "line-1");
            await session.SaveAsync(0, new GameSnapshot { NodeId = "chapter-1" }, CancellationToken.None);

            await session.ClearAsync(CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(session.Variables.GetSnapshot(VariableScope.Player), Is.Empty);
                Assert.That(session.Progress.IsRead("chapter-1", "line-1"), Is.False);
            });
            Assert.That(await session.LoadAsync(0, CancellationToken.None), Is.Null);
            var slots = await session.ListSlotsAsync(CancellationToken.None);
            Assert.That(slots, Has.Count.EqualTo(3));
            Assert.That(slots, Has.All.Matches<SaveSlotInfo>(slot => slot.Timestamp == default && !slot.IsCorrupt));

            session.Dispose();
        }
        finally
        {
            if (Directory.Exists(profileDirectory))
                Directory.Delete(profileDirectory, recursive: true);
        }
    }

    private sealed class EmptyAssetManager : IAssetManager
    {
        public Task<IGameFile?> GetFileAsync(string assetId, CancellationToken ct = default) =>
            Task.FromResult<IGameFile?>(null);

        public Task<IReadOnlyList<IGameFile>> GetFilesAsync(string? typeId = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<IGameFile>>([]);

        public Task<AssetHandle<T>?> AcquireAsync<T>(string assetId, CancellationToken ct = default) where T : class =>
            Task.FromResult<AssetHandle<T>?>(null);

        public Task<AssetHandle<T>?> AcquireByPathAsync<T>(string path, CancellationToken ct = default) where T : class =>
            Task.FromResult<AssetHandle<T>?>(null);

        public void RegisterDecoder<T>(string resourceTypeId, IAssetDecoder<T> decoder) where T : class { }
        public void RegisterProvider(IAssetProvider provider) { }
        public void Dispose() { }
    }
}
