using GalNet.Core.Runtime;
using GalNet.Runtime.Engine;
using GalNet.Runtime.SaveLoad;

namespace GeneralTest.Runtime;

public partial class DebugSessionTests
{
    [Test]
    public async Task GroupEntranceSurvivesSerializationAndRestoreWithoutADebugSession()
    {
        var graph = GraphOf(Entry("test.text", "first"), Entry("test.text", "second"));
        using var view = Registration().RegisterInstruction("test.text", _ => new TextWait()).BuildView();
        var runtime = Runtime();
        await using var engine = new GameEngine(graph, runtime, view);
        runtime.State.Table<DebugState>().Set(new() { Id = "scene", Value = "entrance" });
        runtime.SetVariable("save.counter", 7);
        await engine.AdvanceAsync();
        runtime.State.Table<DebugState>().Set(new() { Id = "scene", Value = "later" });
        runtime.SetVariable("save.counter", 9);
        await engine.AdvanceAsync();
        var restored = SaveManager.Deserialize(SaveManager.Serialize(engine.CreateSaveData()))!;
        Assert.That(restored.GroupEntrance!.Variables["counter"].AsInt(), Is.EqualTo(7));
        await engine.RestoreFromAsync(restored);
        Assert.That(engine.CreateSaveData().GroupEntrance!.States["layers"].Payload.GetRawText(),
            Is.EqualTo(restored.GroupEntrance.States["layers"].Payload.GetRawText()));
        Assert.That(engine.Runtime.GetVariable("counter")!.AsInt(), Is.EqualTo(9));
    }
}
