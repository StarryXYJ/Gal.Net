using System.Text.Json;
using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.Primitives;
using GalNet.Presentation.Defaults;
using GalNet.Runtime.Engine;

namespace GeneralTest.Runtime;

public class GameEnginePrimitiveDispatchTests
{
    [Test]
    public async Task GroupEntriesUseTheSingleDispatcherAndCheckpointStableSnapshot()
    {
        var descriptor = new PrimitiveDescriptor("flow.mark", [], CreatesCheckpoint: true);
        var view = new RecordingView(descriptor);
        var graph = new GalNet.Core.Graph.Graph { RootNodeId = "main", Nodes = { new Group { Id = "main", Entries = { Primitive("flow.mark"), Primitive("flow.mark") } } } };
        var engine = new GameEngine(graph, view);
        var checkpoints = new List<string>();
        engine.CheckpointCreated += snapshot => checkpoints.Add(snapshot.NodeId);

        await engine.StepAsync();

        Assert.That(view.Invocations.Select(item => item.Context.Origin), Is.EqualTo(new[] { PrimitiveInvocationOrigin.GroupEntry, PrimitiveInvocationOrigin.GroupEntry }));
        Assert.That(checkpoints, Is.EqualTo(new[] { "main", "main" }));
        Assert.That(engine.CreateSaveData().EntryIndex, Is.EqualTo(0));
    }

    [Test]
    public async Task UnknownPrimitiveIsSkippedAndExpectedFailuresContinue()
    {
        var view = new RecordingView(new PrimitiveDescriptor("flow.fail", [])) { Fail = true };
        var graph = new GalNet.Core.Graph.Graph { RootNodeId = "main", Nodes = { new Group { Id = "main", Entries = { Primitive("missing.run"), Primitive("flow.fail"), Primitive("flow.fail") } } } };

        var engine = new GameEngine(graph, view);
        await engine.StepAsync();

        Assert.That(view.Invocations, Has.Count.EqualTo(2));
        Assert.That(engine.IsRunning, Is.False);
    }

    [Test]
    public async Task NonBlockingOperationKeepsThePreviousSaveUntilTheRuntimeIsStable()
    {
        var completed = new TaskCompletionSource<PrimitiveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var view = new RecordingView(new PrimitiveDescriptor("flow.async", [])) { Completion = completed.Task, Policy = new(false, false, null) };
        var graph = new GalNet.Core.Graph.Graph
        {
            RootNodeId = "main",
            Nodes = { new Group { Id = "main", Entries = { Primitive("flow.async") } }, new Group { Id = "after" } },
            Edges = { new Edge { FromNodeId = "main", ToNodeId = "after", FromOutlet = 0 } }
        };
        var engine = new GameEngine(graph, view);

        await engine.StepAsync();

        Assert.That(engine.Operations.ActiveOperations, Has.Count.EqualTo(1));
        Assert.That(engine.CreateSaveData().NodeId, Is.EqualTo("main"));
        completed.SetResult(PrimitiveResult.Empty);
        await Task.Yield();
        await Task.Yield();

        Assert.That(engine.Operations.ActiveOperations, Is.Empty);
        Assert.That(engine.CreateSaveData().NodeId, Is.EqualTo("after"));
    }

    private static PrimitiveEntry Primitive(string typeId) => new(typeId, JsonSerializer.SerializeToElement(new { }));

    private sealed class RecordingView(params PrimitiveDescriptor[] descriptors) : NullGameView
    {
        private readonly Dictionary<string, PrimitiveDescriptor> _descriptors = descriptors.ToDictionary(item => item.TypeId, StringComparer.Ordinal);
        public List<PrimitiveInvocation> Invocations { get; } = [];
        public bool Fail { get; init; }
        public Task<PrimitiveResult>? Completion { get; init; }
        public PrimitiveExecutionPolicy? Policy { get; init; }

        public override bool TryGetDescriptor(string primitiveType, out PrimitiveDescriptor? descriptor) => _descriptors.TryGetValue(primitiveType, out descriptor);
        public override PrimitiveDispatch Dispatch(PrimitiveInvocation invocation, PrimitiveExecutionControl control, CancellationToken cancellationToken)
        {
            Invocations.Add(invocation);
            var result = Fail ? PrimitiveResult.Failed : PrimitiveResult.Empty;
            return new PrimitiveDispatch(PrimitiveDispatchStatus.Accepted, Policy ?? new PrimitiveExecutionPolicy(true, false, null), Completion ?? Task.FromResult(result));
        }
    }
}
