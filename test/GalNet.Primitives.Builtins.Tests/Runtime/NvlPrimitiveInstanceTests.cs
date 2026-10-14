using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Presentation.Abstractions.View;
using GalNet.Primitives.Builtins;

namespace GeneralTest.Runtime;

public class NvlPrimitiveInstanceTests
{
    [Test]
    public async Task AppendSkipsTypingThenRequiresAnotherAdvance()
    {
        var presenter = new ControlledNvlPresenter();
        var instance = new NvlAppendPrimitiveInstance(presenter, "one\\skip two", "nvl", CancellationToken.None);

        instance.Dispatch();
        instance.Skip();

        Assert.Multiple(() =>
        {
            Assert.That(instance.IsCompleted, Is.False);
            Assert.That(presenter.SkipCount, Is.EqualTo(1));
            Assert.That(instance.BatchId, Is.EqualTo("nvl"));
        });

        presenter.FinishTyping();
        await Task.Delay(10);
        instance.Skip();

        Assert.That(instance.IsCompleted, Is.True);
    }

    [Test]
    public void VisibilityAndClearEntriesCallOnlyTheirRequestedOperation()
    {
        var presenter = new ControlledNvlPresenter();
        var module = new BuiltinNvlModule(presenter);

        foreach (var entry in new[] { BuiltinEntryIds.ShowNvl, BuiltinEntryIds.HideNvl, BuiltinEntryIds.ClearNvl })
        {
            var primitive = module.Instructions[entry];
            primitive.CreateInstance(new GalNet.Core.Primitives.PrimitiveCreateContext(
                new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules()).Get(entry),
                new GalNet.Core.Entry.PrimitiveEntry(entry, System.Text.Json.JsonSerializer.SerializeToElement(new { })),
                new GalNet.Runtime.Runtime.GameRuntime(null),
                CancellationToken.None)).Dispatch();
        }

        Assert.That(presenter.Operations, Is.EqualTo(["show", "hide", "clear"]));
    }

    private sealed class ControlledNvlPresenter : INvlPresenter
    {
        private readonly TaskCompletionSource _typing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> Operations { get; } = [];
        public int SkipCount { get; private set; }

        public void ShowNvl() => Operations.Add("show");
        public void HideNvl() => Operations.Add("hide");
        public void ClearNvl() => Operations.Add("clear");
        public Task AppendNvlTextAsync(string text, CancellationToken cancellationToken) => _typing.Task.WaitAsync(cancellationToken);
        public void SkipNvlText() => SkipCount++;
        public void RevealAllNvlText() => FinishTyping();
        public Task AppendNvlTextImmediatelyAsync(string text, CancellationToken cancellationToken) => Task.CompletedTask;
        public void FinishTyping() => _typing.TrySetResult();
    }
}
