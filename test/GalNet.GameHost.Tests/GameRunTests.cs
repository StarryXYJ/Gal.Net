using System.Text.Json;
using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.Primitives;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;
using GalNet.Runtime.Engine;
using GalNet.Runtime.Runtime;

namespace GalNet.GameHost.Tests;

public sealed class GameRunTests
{
    [Test]
    public async Task CustomInstructionRunsWithoutBuiltinsOrLayerPresentersAndViewRemainsHostOwned()
    {
        var calls = 0;
        var restores = 0;
        using var registration = new GameRegistration();
        registration.RegisterSchema(new DefaultEntryModule("custom",
            [new DefaultPrimitiveEntryBase("custom.run", DynamicParameterTable.Empty)]));
        registration.RegisterImplementation(new PrimitiveModule(registry =>
        {
            registry.Register("custom.run", _ => new ImmediatePrimitiveInstance(() => calls++));
            registry.Restore("custom.restore", _ => { restores++; return Task.CompletedTask; });
        }));
        await using var view = registration.BuildView();
        var graph = new Graph
        {
            RootNodeId = "main",
            Nodes =
        {
            new Group { Id = "main", Entries = [new PrimitiveEntry("custom.run", JsonSerializer.SerializeToElement(new { }))] }
        }
        };
        await using var run = new GameRun(graph, new GameRuntime(null, "main"), view);
        await run.PrepareAsync(run.CreateSaveData());
        Assert.That(restores, Is.EqualTo(1));
        Assert.That(calls, Is.Zero);
        await run.StartPreparedAsync();
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(run.Engine.State, Is.EqualTo(GameEngineState.Completed));
        await run.DisposeAsync();
        Assert.That(view.CanExecute("custom.run"), Is.True);
    }
}
