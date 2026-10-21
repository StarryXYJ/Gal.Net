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
    }
}
