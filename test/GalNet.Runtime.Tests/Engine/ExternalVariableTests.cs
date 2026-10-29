using System.Text.Json;
using GalNet.Core.Entry;
using GalNet.Core.Execution;
using GalNet.Core.Graph;
using GalNet.Core.Primitives;
using GalNet.Core.Runtime;
using GalNet.Core.Variable;
using GalNet.Runtime.Engine;
using GalNet.Runtime.Persistence;
using GalNet.Runtime.Runtime;
using GalNet.Runtime.Variables;

namespace GeneralTest.Runtime;

public sealed class ExternalVariableTests
{
    [Test]
    public async Task EditsDuringBlockingPresentationPreserveCursorAndUpdateImmediateSave()
    {
        var runtime = new GameRuntime(null, "main");
        runtime.SetVariable("save.score", 1);
        await using var engine = Create(runtime);
        var before = engine.CreateSaveData();
        await engine.AdvanceAsync();

        var observerWasOutsideGate = false;
        engine.CheckpointCreated += _ => observerWasOutsideGate = engine.UpdatePlaybackOptionsAsync(new PlaybackOptions()).IsCompletedSuccessfully;
        await engine.SetVariablesAsync(new Dictionary<string, object> { ["save.score"] = 9, ["player.unlocked"] = true });
        var saved = engine.CreateSaveData();
        Assert.Multiple(() =>
        {
            Assert.That(observerWasOutsideGate, Is.True);
            Assert.That(runtime.GetVariable("save.score")!.AsInt(), Is.EqualTo(9));
            Assert.That(saved.Variables["score"].AsInt(), Is.EqualTo(9));
            Assert.That(saved.Variables["score"].Uid, Is.EqualTo(before.Variables["score"].Uid));
            Assert.That(saved.EntryIndex, Is.EqualTo(before.EntryIndex));
            Assert.That(saved.Variables.ContainsKey("unlocked"), Is.False);
            Assert.That(engine.State, Is.EqualTo(GameEngineState.Running));
        });
        await engine.RestoreFromAsync(saved);
        Assert.That(runtime.GetVariable("save.score")!.AsInt(), Is.EqualTo(9));
        Assert.That(runtime.GetVariable("player.unlocked")!.AsBool(), Is.True);
    }

    [Test]
    public async Task ConditionalReadModifyWriteUsesLatestValuesAndRejectedOperationDoesNotFaultRun()
    {
        var runtime = new GameRuntime(null, "main");
        runtime.SetVariable("save.coins", 0);
        await using var engine = Create(runtime);
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() =>
            engine.UpdateVariablesAsync(current => new Dictionary<string, object> { ["save.coins"] = current["save.coins"].AsInt() + 1 }))));
        Assert.That(engine.CreateSaveData().Variables["coins"].AsInt(), Is.EqualTo(20));
        Assert.ThrowsAsync<InvalidOperationException>(() => engine.UpdateVariablesAsync(_ => throw new InvalidOperationException("Insufficient funds.")));
        Assert.That(runtime.GetVariable("save.coins")!.AsInt(), Is.EqualTo(20));
        Assert.That(engine.State, Is.EqualTo(GameEngineState.Ready));
    }

    [TestCase("score", 2)]
    [TestCase("save.", 2)]
    [TestCase("save.score", double.PositiveInfinity)]
    [TestCase("save.score", long.MaxValue)]
    public async Task InvalidBatchDoesNotApplyEarlierValues(string name, object value)
    {
        var runtime = new GameRuntime(null, "main");
        await using var engine = Create(runtime);
        Assert.ThrowsAsync<ArgumentException>(() => engine.SetVariablesAsync(new Dictionary<string, object>
        {
            ["save.valid"] = 1,
            [name] = value
        }));
        Assert.That(runtime.GetVariable("save.valid"), Is.Null);
        Assert.That(engine.CreateSaveData().Variables, Is.Empty);
    }

    [Test]
    public async Task PlayerWritesPreserveStorageIdentityAndCanBeReopened()
    {
        var initial = new Variable { Name = "player.coins", Value = VariableValue.From(1) };
        var store = new MemoryStore { Saved = new() { [initial.Name] = initial } };
        using var player = await PlayerVariableService.CreateAsync(store);
        player.VariableChanged += (_, _, _) => throw new IOException("Observer failed.");
        await using var engine = Create(new GameRuntime(null, "main", player));
        await engine.SetVariablesAsync(new Dictionary<string, object> { ["player.coins"] = 12 });
        await player.FlushPlayerVariablesAsync();
        using var reopened = await PlayerVariableService.CreateAsync(store);
        var saved = reopened.GetSnapshot(VariableScope.Player);
        Assert.That(saved.Keys, Is.EqualTo(new[] { "player.coins" }));
        Assert.That(saved[initial.Name].Uid, Is.EqualTo(initial.Uid));
        Assert.That(saved[initial.Name].AsInt(), Is.EqualTo(12));
    }

    [Test]
    public async Task QueuedEditCannotCrossRestoreOrStop()
    {
        await using var engine = Create(new GameRuntime(null, "main"));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var presentation = engine.SynchronizePresentationAsync(async (_, _) =>
        {
            entered.SetResult();
            await release.Task;
        });
        await entered.Task;
        var restore = engine.RestoreFromAsync(engine.CreateSaveData());
        var edit = engine.SetVariablesAsync(new Dictionary<string, object> { ["save.stale"] = true });
        release.SetResult();
        await presentation;
        await restore;
        Assert.ThrowsAsync<OperationCanceledException>(() => edit);
        Assert.That(engine.Runtime.GetVariable("save.stale"), Is.Null);
        await engine.StopAsync();
        Assert.ThrowsAsync<OperationCanceledException>(() => engine.SetVariablesAsync(new Dictionary<string, object> { ["save.stale"] = true }));
    }

    private static GameEngine Create(GameRuntime runtime) => new(new Graph
    {
        RootNodeId = "main",
        Nodes = [new Group { Id = "main", Entries = [new PrimitiveEntry("project.wait", JsonSerializer.SerializeToElement(new { }))] }]
    }, runtime, new Dispatcher());

    private sealed class Dispatcher : IPrimitiveDispatcher
    {
        public IReadOnlyCollection<EntryDefinition> Primitives => [];
        public bool CanExecute(string primitiveType) => primitiveType == "project.wait";
        public bool TryGetEntry(string primitiveType, out EntryDefinition? entry) { entry = null; return false; }
        public PrimitiveInstance Dispatch(PrimitiveEntry entry, IGameContext runtime, CancellationToken cancellationToken,
            PrimitiveInvocationMode mode = PrimitiveInvocationMode.Normal)
        {
            var instance = new Wait();
            instance.Dispatch(mode);
            return instance;
        }
    }

    private sealed class Wait : PrimitiveInstance
    {
        public override bool IsBlocking => true;
        public override bool IsSkippable => false;
        protected override void OnDispatch() { }
    }

    private sealed class MemoryStore : IPlayerVariableStore
    {
        public Dictionary<string, Variable> Saved { get; set; } = [];
        public Task<IReadOnlyDictionary<string, Variable>> LoadAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyDictionary<string, Variable>>(Saved);
        public Task SaveAsync(IReadOnlyDictionary<string, Variable> values, CancellationToken ct = default)
        {
            Saved = values.ToDictionary(pair => pair.Key, pair => pair.Value.Clone());
            return Task.CompletedTask;
        }
    }
}
