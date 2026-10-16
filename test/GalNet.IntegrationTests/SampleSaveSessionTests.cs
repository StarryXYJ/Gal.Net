using GalNet.Core.Assets;
using GalNet.Core.Graph;
using GalNet.Core.Runtime;
using GalNet.Core.Variable;
using GalNet.Runtime.Content;
using GalNet.Runtime.Persistence;
using GalNet.Sample.Avalonia.Services;
using GalVariable = GalNet.Core.Variable.Variable;

namespace GalNet.IntegrationTests;

public sealed class SampleSaveSessionTests
{
    [Test]
    public async Task FirstEmptySavesSerializeAndNeverOverwriteOccupiedOrCorruptSlots()
    {
        var directory = Path.Combine(Path.GetTempPath(), "galnet-empty-slot-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var assets = new EmptyAssetManager();
            using var session = await SampleSaveSession.CreateAsync(directory, new GameContent { Graph = new Graph() }, assets, CancellationToken.None);
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
            using var session = await SampleSaveSession.CreateAsync(
                profileDirectory,
                new GameContent { Graph = new Graph() },
                assets,
                CancellationToken.None);
            var originalSettings = session.Settings;
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
                Assert.That(session.Settings, Is.Not.SameAs(originalSettings));
            });
            Assert.That(await session.LoadAsync(0, CancellationToken.None), Is.Null);
            Assert.That(
                await session.ListSlotsAsync(CancellationToken.None),
                Has.All.Matches<SaveSlotInfo>(slot => slot.Timestamp == default && !slot.IsCorrupt));

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
