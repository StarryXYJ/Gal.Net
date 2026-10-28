using System.Text.Json;
using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.Primitives;
using GalNet.Core.Runtime;
using GalNet.Core.Variable;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;
using GalNet.Runtime.Runtime;

namespace GalNet.GameHost.Tests;

public sealed class NewGameVariablesTests
{
    [Test]
    public async Task StartupParametersAreAppliedBeforeFirstInstructionAndNotReappliedOnRestore()
    {
        var chapters = new List<int>();
        using var registration = new GameRegistration();
        registration.RegisterSchema(new DefaultEntryModule("chapter", [new DefaultPrimitiveEntryBase("chapter.observe", DynamicParameterTable.Empty)]));
        registration.RegisterImplementation(new PrimitiveModule(registry => registry.Register("chapter.observe", context =>
            new ImmediatePrimitiveInstance(() => chapters.Add(context.Runtime.GetVariable("chapter")!.AsInt())))));
        await using var view = registration.BuildView();
        var graph = new Graph { RootNodeId = "main", Nodes = [new Group { Id = "main", Entries =
            [new PrimitiveEntry("chapter.observe", JsonSerializer.SerializeToElement(new { }))] }] };
        var defaults = new List<ProjectVariableDefinition> { new() { Name = "chapter", DefaultValue = new() { Uid = "chapter-id", Name = "chapter", Value = VariableValue.From(1) } } };
        GameRun? current = null;
        await using var session = new GameSession(_ =>
        {
            current = new GameRun(graph, new GameRuntime(null, "main", saveDefaults: defaults), view);
            return Task.FromResult<IGameRun>(current);
        });
        await session.PrepareNewAsync(new Dictionary<string, object> { ["save.chapter"] = 2 });
        Assert.That(chapters, Is.Empty);
        var saved = current!.CreateSaveData();
        Assert.That(saved.Variables["chapter"].Uid, Is.EqualTo("chapter-id"));
        Assert.That(saved.Variables["chapter"].AsInt(), Is.EqualTo(2));
        await session.BeginPreparedAsync();
        Assert.That(chapters, Is.EqualTo(new[] { 2 }));
        await session.PrepareAsync(null);
        Assert.That(current!.CreateSaveData().Variables["chapter"].AsInt(), Is.EqualTo(1));
        await session.PrepareAsync(saved);
        Assert.That(current!.CreateSaveData().Variables["chapter"].AsInt(), Is.EqualTo(2));
        await session.BeginPreparedAsync();
        Assert.That(chapters, Is.EqualTo(new[] { 2, 2 }));
    }

    [Test]
    public void StartupBatchRejectsPlayerScopeInvalidValuesAndCopiesTheInput()
    {
        Assert.Throws<ArgumentException>(() => NewGameVariables.Capture(new Dictionary<string, object> { ["player.chapter"] = 1 }));
        Assert.Throws<ArgumentException>(() => NewGameVariables.Capture(new Dictionary<string, object> { ["save.chapter"] = 1, ["save.bad"] = double.NaN }));
        var source = new Dictionary<string, object> { ["save.chapter"] = 2 };
        var snapshot = NewGameVariables.Capture(source);
        source["save.chapter"] = 9;
        Assert.That(snapshot["save.chapter"], Is.EqualTo(2));
    }
}
