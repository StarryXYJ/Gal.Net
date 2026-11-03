using System.Text.Json;
using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.Primitives;
using GalNet.Presentation.Abstractions.View;
using GalNet.Runtime.Engine;

namespace GalNet.Debugging.Tests;

public class EngineProtocolTests
{
    [Test]
    public async Task ContentCommandsExposeVersionsAndActivateTheInstalledGraph()
    {
        var calls = new List<string>();
        using var registration = new GameRegistration().RegisterSchema(new DefaultEntryModule("test",
            [new DefaultPrimitiveEntryBase("test.wait", DynamicParameterTable.Empty),
             new DefaultPrimitiveEntryBase("test.run", DynamicParameterTable.Empty)]));
        using var view = registration.RegisterInstruction("test.wait", _ => new Wait())
            .RegisterInstruction("test.run", context => new ImmediatePrimitiveInstance(() => calls.Add(context.Entry.StableId))).BuildView();
        var initial = GraphWith(new PrimitiveEntry("test.wait", JsonSerializer.SerializeToElement(new { })) { StableId = "active" },
            new PrimitiveEntry("test.run", JsonSerializer.SerializeToElement(new { })) { StableId = "old" });
        var candidate = GraphWith(new PrimitiveEntry("test.wait", JsonSerializer.SerializeToElement(new { })) { StableId = "active" },
            new PrimitiveEntry("test.run", JsonSerializer.SerializeToElement(new { })) { StableId = "new" });
        await using var engine = new GameEngine(initial, view);
        var revisions = new FakeContentRevisions(candidate);
        using var adapter = new EngineDebugRun(engine, content: revisions);
        await engine.AdvanceAsync();
        var run = adapter.Info;
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        async Task<T> Send<T>(string method, object? value = null)
        {
            var result = await adapter.ExecuteAsync(new(method, run.RunId, adapter.Info.Generation,
                JsonSerializer.SerializeToElement(value, json)), CancellationToken.None);
            return (T)result!;
        }

        var installed = await Send<DebugContentVersions>("content.install",
            new DebugContentInstallRequest("candidate", "candidate", "content", "contract"));
        var before = await Send<GameDebugState>("inspect");
        var activated = await Send<DebugContentActivationResult>("content.activate",
            new DebugContentActivateRequest("candidate",
                new(before.NodeId, before.EntryIndex, before.ActiveEntryIds), CandidateEntryIndex: 1));
        await engine.AdvanceAsync();
        var after = await Send<GameDebugState>("inspect");

        Assert.Multiple(() =>
        {
            Assert.That(adapter.Info.Capabilities, Does.Contain("content.install").And.Contain("content.activate"));
            Assert.That(installed, Is.EqualTo(new DebugContentVersions("candidate", "initial")));
            Assert.That(activated.Versions, Is.EqualTo(new DebugContentVersions("candidate", "candidate")));
            Assert.That(calls, Is.EqualTo(new[] { "new" }));
            Assert.That(after.LatestRevision, Is.EqualTo("candidate"));
            Assert.That(after.RunRevision, Is.EqualTo("candidate"));
        });
    }

    [Test]
    public async Task ActivateAndSeekUsesTheSavedEntranceWithTheInstalledGraph()
    {
        var calls = new List<string>();
        using var registration = new GameRegistration().RegisterSchema(new DefaultEntryModule("test",
            [new DefaultPrimitiveEntryBase("test.run", DynamicParameterTable.Empty),
             new DefaultPrimitiveEntryBase("test.wait", DynamicParameterTable.Empty)]));
        using var view = registration.RegisterInstruction("test.run", context =>
                new ImmediatePrimitiveInstance(() => calls.Add(context.Entry.StableId)))
            .RegisterInstruction("test.wait", _ => new Wait()).BuildView();
        var initial = GraphWith(new PrimitiveEntry("test.run", JsonSerializer.SerializeToElement(new { })) { StableId = "old" },
            new PrimitiveEntry("test.wait", JsonSerializer.SerializeToElement(new { })) { StableId = "wait" });
        var candidate = GraphWith(new PrimitiveEntry("test.run", JsonSerializer.SerializeToElement(new { })) { StableId = "new" },
            new PrimitiveEntry("test.wait", JsonSerializer.SerializeToElement(new { })) { StableId = "wait" });
        await using var engine = new GameEngine(initial, view);
        var revisions = new FakeContentRevisions(candidate);
        using var adapter = new EngineDebugRun(engine,
            new() { Instructions = new Dictionary<string, DebugReplayBehavior> { ["test.run"] = DebugReplayBehavior.Scene, ["test.wait"] = DebugReplayBehavior.Scene } },
            content: revisions);
        await engine.AdvanceAsync();
        calls.Clear();
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        async Task<object?> Send(string method, object value) => await adapter.ExecuteAsync(new(method, adapter.Info.RunId,
            adapter.Info.Generation, JsonSerializer.SerializeToElement(value, json)), CancellationToken.None);
        await Send("content.install", new DebugContentInstallRequest("candidate", "candidate", "content", "contract"));
        var state = (GameDebugState)(await adapter.ExecuteAsync(new("inspect", adapter.Info.RunId, adapter.Info.Generation,
            JsonSerializer.SerializeToElement(new { }, json)), CancellationToken.None))!;

        await Send("content.activateAndSeekGroupStart", new DebugContentActivateRequest("candidate",
            new(state.NodeId, state.EntryIndex, state.ActiveEntryIds), CandidateEntryIndex: state.EntryIndex));

        Assert.That(calls, Is.EqualTo(new[] { "new" }));
        Assert.That(revisions.Versions.RunRevision, Is.EqualTo("candidate"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task StopAndDisconnectDoNotWaitBehindAnUnskippableReplay(bool disconnect)
    {
        using var registration = new GameRegistration().RegisterSchema(new DefaultEntryModule("test",
            [new DefaultPrimitiveEntryBase("test.wait", DynamicParameterTable.Empty)]));
        var blocker = new ReplayWait();
        using var view = registration.RegisterInstruction("test.wait", _ => blocker).BuildView();
        var graph = new Graph { RootNodeId = "main", Nodes = { new Group { Id = "main", Entries = { new PrimitiveEntry("test.wait", JsonSerializer.SerializeToElement(new { })) { StableId = "wait#1" } } } } };
        await using var engine = new GameEngine(graph, view);
        using var adapter = new EngineDebugRun(engine, new() { Instructions = new Dictionary<string, DebugReplayBehavior> { ["test.wait"] = DebugReplayBehavior.Scene } });
        await using var host = new GameDebugHost();
        using var binding = host.AttachRun(adapter);
        using var listener = new DebugListener(new("project", "content"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var accepted = listener.AcceptAsync(timeout.Token);
        await host.ConnectAsync(listener.Invitation, timeout.Token);
        await using var peer = await accepted;
        var run = adapter.Info;
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var seek = peer.RequestAsync("seek", new DebugCommand("seek", run.RunId, run.Generation,
            JsonSerializer.SerializeToElement(new GameDebugSeek("main", 1, []), json)), timeout.Token);
        await blocker.Started.Task.WaitAsync(timeout.Token);
        if (disconnect)
        {
            await peer.DisposeAsync();
            await host.DisconnectAsync().WaitAsync(timeout.Token);
            Assert.That(engine.State, Is.EqualTo(GameEngineState.Running));
            var state = (GameDebugState)(await adapter.ExecuteAsync(new("inspect", run.RunId, adapter.Info.Generation,
                JsonSerializer.SerializeToElement(new { })), timeout.Token))!;
            Assert.That(state.Paused, Is.False);
        }
        else
        {
            await peer.RequestAsync("stop", new DebugCommand("stop", run.RunId, run.Generation, JsonSerializer.SerializeToElement(new { })), timeout.Token);
            Assert.That(engine.State, Is.EqualTo(GameEngineState.Stopped));
        }
        Exception? failure = null;
        try { await seek.WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (Exception error) { failure = error; }
        Assert.That(failure, Is.InstanceOf<IOException>().Or.InstanceOf<InvalidOperationException>().Or.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task RealConnectionReadsWritesAndSeeksTheBoundEngineWithGenerationChecks()
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        using var registration = new GameRegistration().RegisterSchema(new DefaultEntryModule("test",
            [new DefaultPrimitiveEntryBase("test.text", DynamicParameterTable.Empty) { IsReading = true }]));
        using var view = registration.RegisterInstruction("test.text", _ => new Wait()).BuildView();
        var graph = new Graph { RootNodeId = "main", Nodes = { new Group { Id = "main", Entries = { new PrimitiveEntry("test.text", JsonSerializer.SerializeToElement(new { })) { StableId = "line#1" } } } } };
        await using var engine = new GameEngine(graph, view);
        engine.Runtime.SetVariable("save.score", 4);
        using var adapter = new EngineDebugRun(engine, new() { Instructions = new Dictionary<string, DebugReplayBehavior> { ["test.text"] = DebugReplayBehavior.Scene } });
        await using var host = new GameDebugHost();
        using var binding = host.AttachRun(adapter);
        using var listener = new DebugListener(new("project", "content"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var accepted = listener.AcceptAsync(timeout.Token);
        await host.ConnectAsync(listener.Invitation, timeout.Token);
        await using var peer = await accepted;
        await engine.AdvanceAsync();
        async Task<JsonElement> Send(string method, object? arguments = null, long? generation = null) =>
            await peer.RequestAsync(method, new DebugCommand(method, adapter.Info.RunId, generation ?? adapter.Info.Generation, JsonSerializer.SerializeToElement(arguments, json)), timeout.Token);
        var state = (await Send("inspect")).Deserialize<GameDebugState>(json)!;
        await Send("setVariable", new DebugVariableEdit(state.Variables.Single(), "9"));
        Assert.That(engine.Runtime.GetVariable("score")!.AsInt(), Is.EqualTo(9));
        await Send("seek", new GameDebugSeek("main", 0, []));
        Assert.That(engine.EntryIndex, Is.Zero);
        Assert.That((await Send("inspect")).GetProperty("paused").GetBoolean(), Is.True);
        Assert.ThrowsAsync<InvalidOperationException>(async () => await Send("resume", generation: state.Generation));
        await Send("resume");
        Assert.That(engine.EntryIndex, Is.EqualTo(1));
        await Send("stop");
        Assert.That((await peer.RequestAsync("status", cancellationToken: timeout.Token)).GetProperty("run").ValueKind, Is.EqualTo(JsonValueKind.Null));
    }
    private sealed class Wait : PrimitiveInstance
    {
        public override bool IsBlocking => true;
        public override bool IsSkippable => true;
        protected override void OnDispatch() { }
        protected override void OnPlaybackModeChanged(PrimitiveInvocationMode mode) { if (mode == PrimitiveInvocationMode.CatchUp) TryComplete(); }
        protected override void OnSkip() => TryComplete();
    }
    private sealed class ReplayWait : PrimitiveInstance
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool IsBlocking => true;
        public override bool IsSkippable => false;
        protected override void OnDispatch() => Started.TrySetResult();
    }

    private static Graph GraphWith(params GalNet.Core.Entry.Entry[] entries) => new()
    {
        RootNodeId = "main",
        Nodes = { new Group { Id = "main", Entries = entries.ToList() } }
    };

    private sealed class FakeContentRevisions(Graph graph) : IDebugContentRevisionController
    {
        public DebugContentVersions Versions { get; private set; } = new("initial", "initial");
        public Task<DebugContentVersions> InstallAsync(DebugContentInstallRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Versions = Versions with { LatestRevision = request.Revision };
            return Task.FromResult(Versions);
        }
        public Graph GetLatestGraph(string revision) =>
            revision == Versions.LatestRevision ? graph : throw new InvalidOperationException();
        public void CommitActivation(string revision) => Versions = new(revision, revision);
    }
}
