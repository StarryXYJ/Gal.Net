using System.Text.Json;
using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.Primitives;
using GalNet.Presentation.Abstractions.View;
using GalNet.Runtime.Engine;

namespace GeneralTest.Runtime;

public sealed class PrimitivePresentationFailureTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task CompletedAsyncFailure_StopsStoryAndReportsEngineFault(bool failDuringDispatch)
    {
        var instance = new FailedPresentation(failDuringDispatch);
        var catalog = new TargetProfileEntryCatalog([new DefaultEntryModule("test", [new DefaultPrimitiveEntryBase("test.capture", DynamicParameterTable.Empty)])]);
        using var registration = new GameRegistration().UseCatalog(catalog).RegisterInstruction("test.capture", _ => instance);
        await using var view = registration.BuildView();
        var group = new Group { Id = "g", Entries = [new PrimitiveEntry("test.capture", JsonSerializer.SerializeToElement(new { })) { StableId = "capture" }] };
        await using var engine = new GameEngine(new Graph { RootNodeId = group.Id, Nodes = [group] }, view);
        var fault = new TaskCompletionSource<GameEngineStateChange>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.StateChanged += change => { if (change.State == GameEngineState.Faulted) fault.TrySetResult(change); };
        if (failDuringDispatch) Assert.ThrowsAsync<IOException>(() => engine.AdvanceAsync());
        else { await engine.AdvanceAsync(); instance.Fail(); }
        Assert.That((await fault.Task.WaitAsync(TimeSpan.FromSeconds(2))).Error!.Message, Is.EqualTo("capture failed"));
        Assert.That(engine.State, Is.EqualTo(GameEngineState.Faulted));
    }

    private sealed class FailedPresentation(bool immediate) : AsyncPrimitiveInstance(blocking: true)
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override Task ExecuteAsync(CancellationToken token) =>
            immediate ? Task.FromException(new IOException("capture failed")) : _completion.Task;
        public void Fail() => _completion.SetException(new IOException("capture failed"));
    }
}
