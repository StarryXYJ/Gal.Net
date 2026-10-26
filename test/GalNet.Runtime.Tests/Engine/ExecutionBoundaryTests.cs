using System.Text.Json;
using GalNet.Core.Entry;
using GalNet.Core.Execution;
using GalNet.Core.Graph;
using GalNet.Core.Primitives;
using GalNet.Core.Runtime;
using GalNet.Runtime.Engine;
using GalNet.Runtime.Runtime;

namespace GeneralTest.Runtime;

public sealed class ExecutionBoundaryTests
{
    [Test]
    public async Task CustomDispatcher_CanExecuteAndRestoreWithoutPresentationServices()
    {
        var group = new Group
        {
            Id = "entry",
            Entries = [new PrimitiveEntry("project.increment", JsonSerializer.SerializeToElement(new { }))]
        };
        var runtime = new GameRuntime(null, group.Id);
        runtime.SetVariable("save.count", 0);
        await using var engine = new GameEngine(new Graph { RootNodeId = group.Id, Nodes = [group] }, runtime, new CounterDispatcher());
        var start = engine.CreateSaveData();

        await engine.AdvanceAsync();
        Assert.That(engine.Runtime.GetVariable("save.count")!.AsInt(), Is.EqualTo(1));
        Assert.That(engine.State, Is.EqualTo(GameEngineState.Completed));

        await engine.RestoreFromAsync(start);
        Assert.That(engine.Runtime.GetVariable("save.count")!.AsInt(), Is.Zero);
        await engine.AdvanceAsync();
        Assert.That(engine.Runtime.GetVariable("save.count")!.AsInt(), Is.EqualTo(1));
    }

    [Test]
    public void RuntimeAssembly_DoesNotDependOnPresentationOrBuiltinImplementations()
    {
        var references = typeof(GameEngine).Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
        Assert.That(references.Where(name => name is not null &&
            (name.StartsWith("GalNet.Presentation.", StringComparison.Ordinal) ||
             name.StartsWith("GalNet.Avalonia.", StringComparison.Ordinal) ||
             name is "GalNet.Primitives.Builtins" or "GalNet.Contracts.Builtins")), Is.Empty);
        Assert.That(typeof(IGameContext).GetProperties().Select(property => property.Name), Does.Not.Contain("SceneInstances"));
        Assert.That(typeof(GameState).Assembly.GetExportedTypes().Select(type => type.Namespace),
            Does.Not.Contain("GalNet.Core.Scene").And.Not.Contain("GalNet.Core.Audio").And.Not.Contain("GalNet.Core.Gallery").And.Not.Contain("GalNet.Core.Variants"));
    }

    private sealed class CounterDispatcher : IPrimitiveDispatcher
    {
        public IReadOnlyCollection<EntryDefinition> Primitives => [];
        public bool CanExecute(string primitiveType) => primitiveType == "project.increment";
        public bool TryGetEntry(string primitiveType, out EntryDefinition? entry) { entry = null; return false; }

        public PrimitiveInstance Dispatch(PrimitiveEntry entry, IGameContext runtime, CancellationToken cancellationToken,
            PrimitiveInvocationMode mode = PrimitiveInvocationMode.Normal)
        {
            var instance = new ImmediatePrimitiveInstance(() =>
                runtime.SetVariable("save.count", runtime.GetVariable("save.count")!.AsInt() + 1), batchId: entry.BatchId);
            instance.Dispatch(mode);
            return instance;
        }
    }
}
