using GalNet.Editor.History;

namespace GeneralTest.Editor;

public sealed class ProjectHistoryFailureTests
{
    [Test]
    public void FailedEditAfterUndoDoesNotLoseTheRedoBranch()
    {
        using var history = new UndoRedoHistory();
        var value = 0;
        history.Execute(new PropertyEdit<int>("one", next => value = next, 0, 1));
        history.Undo();
        Assert.Throws<IOException>(() => history.Execute(new DelegateEdit("failure", () => { }, () => throw new IOException())));
        Assert.That(history.RedoDescription, Is.EqualTo("one"));
        history.Redo();
        Assert.That(value, Is.EqualTo(1));
    }

    [Test]
    public void FailedUndoOrRedoDoesNotAdvanceTheCursor()
    {
        using var history = new UndoRedoHistory();
        var failUndo = true;
        var failRedo = false;
        var value = 0;
        history.Execute(new DelegateEdit("edit",
            () => { if (failUndo) throw new IOException(); value = 0; },
            () => { if (failRedo) throw new IOException(); value = 1; }));
        Assert.Throws<IOException>(history.Undo);
        Assert.That(history.UndoDescription, Is.EqualTo("edit"));
        Assert.That(value, Is.EqualTo(1));
        failUndo = false;
        history.Undo();
        failRedo = true;
        Assert.Throws<IOException>(history.Redo);
        Assert.That(history.RedoDescription, Is.EqualTo("edit"));
        Assert.That(value, Is.Zero);
    }

    [Test]
    public void CompositeExecutionAndUndoCompensateCompletedSteps()
    {
        using var history = new UndoRedoHistory();
        var value = 0;
        Assert.Throws<IOException>(() => history.Execute(new CompositeEdit("failure",
        [
            new PropertyEdit<int>("one", next => value = next, 0, 1),
            new DelegateEdit("fail", () => { }, () => throw new IOException())
        ])));
        Assert.That(value, Is.Zero);
        Assert.That(history.CanUndo, Is.False);
        history.Execute(new CompositeEdit("undo failure",
        [
            new DelegateEdit("fail undo", () => throw new IOException(), () => value++),
            new DelegateEdit("two", () => value--, () => value++)
        ]));
        Assert.Throws<IOException>(history.Undo);
        Assert.That(value, Is.EqualTo(2));
        Assert.That(history.UndoDescription, Is.EqualTo("undo failure"));
    }

    [Test]
    public void CompensationFailureBlocksFurtherHistoryWrites()
    {
        using var history = new UndoRedoHistory();
        Assert.Throws<HistoryRepairRequiredException>(() => history.Execute(new CompositeEdit("broken",
        [
            new DelegateEdit("cannot compensate", () => throw new IOException(), () => { }),
            new DelegateEdit("fail", () => { }, () => throw new IOException())
        ])));
        Assert.That(history.RepairRequired, Is.True);
        Assert.That(history.CanUndo, Is.False);
        Assert.Throws<InvalidOperationException>(() => history.Execute(new DelegateEdit("later", () => { }, () => { })));
    }

    [Test]
    public async Task AsyncEditsSerializeAndRejectReentryWithoutBlockingTheCaller()
    {
        using var history = new UndoRedoHistory();
        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var values = new List<int>();
        var first = history.ExecuteAsync(new AsyncEdit("first", async () =>
        {
            started.SetResult();
            await release.Task;
            values.Add(1);
        }, () => { values.Remove(1); return ValueTask.CompletedTask; }));
        await started.Task;
        var second = history.ExecuteAsync(new AsyncEdit("second", () => { values.Add(2); return ValueTask.CompletedTask; }, () => { values.Remove(2); return ValueTask.CompletedTask; }));
        Assert.That(history.IsBusy, Is.True);
        Assert.That(second.IsCompleted, Is.False);
        release.SetResult();
        await Task.WhenAll(first, second);
        Assert.That(values, Is.EqualTo(new List<int> { 1, 2 }));
        await history.UndoAsync();
        Assert.That(values.Single(), Is.EqualTo(1));
        try
        {
            await history.ExecuteAsync(new AsyncEdit("reentry", async () => await history.UndoAsync(), () => ValueTask.CompletedTask));
            Assert.Fail("Nested history must not deadlock or execute.");
        }
        catch (InvalidOperationException) { }
        Assert.That(history.RedoDescription, Is.EqualTo("second"));
    }

    [Test]
    public async Task AsyncCompositeCompensatesBeforeReturningFailure()
    {
        using var history = new UndoRedoHistory();
        var value = 0;
        try
        {
            await history.ExecuteAsync(new AsyncCompositeEdit("failure",
            [
                new AsyncEdit("one", () => { value++; return ValueTask.CompletedTask; }, () => { value--; return ValueTask.CompletedTask; }),
                new AsyncEdit("fail", () => throw new IOException(), () => ValueTask.CompletedTask)
            ]));
            Assert.Fail("Injected edit must fail.");
        }
        catch (IOException) { }
        Assert.That(value, Is.Zero);
        Assert.That(history.CanUndo, Is.False);
    }

    [Test]
    public async Task CloseSuspensionWaitsForActiveEditAndRefusesQueuedMutation()
    {
        using var history = new UndoRedoHistory();
        var release = new TaskCompletionSource();
        var executing = history.ExecuteAsync(new AsyncEdit("wait", async () => await release.Task, () => ValueTask.CompletedTask));
        var closing = history.SuspendAsync();
        Assert.That(closing.IsCompleted, Is.False);
        release.SetResult();
        await Task.WhenAll(executing, closing);
        Assert.That(history.CanUndo, Is.False);
        history.Resume();
        Assert.That(history.CanUndo, Is.True);
    }

    [Test]
    public void HistoryPrunesByStepAndRetainedByteBudgets()
    {
        using var history = new UndoRedoHistory(capacity: 3, byteBudget: 10);
        var value = 0;
        for (var index = 0; index < 4; index++) history.Execute(new SizedEdit(() => value++, () => value--));
        history.Undo();
        history.Undo();
        Assert.That(value, Is.EqualTo(3), "Only the newest 8-byte edit fits the 10-byte soft budget.");
    }

    [Test]
    public void TrimmingTruncatingAndClearingReleaseOnlyUnreferencedHistoryResources()
    {
        using var history = new UndoRedoHistory(capacity: 1);
        var released = 0;
        history.Execute(new LeasedEdit(() => released++));
        history.Execute(new LeasedEdit(() => released++));
        Assert.That(released, Is.EqualTo(1));
        history.Undo();
        history.Execute(new LeasedEdit(() => released++));
        Assert.That(released, Is.EqualTo(2));
        history.Clear();
        Assert.That(released, Is.EqualTo(3));
    }

    private sealed record LeasedEdit(Action Release) : IUndoableEdit, IDisposable
    {
        public string Description => "lease";
        public void Undo() { }
        public void Redo() { }
        public void Dispose() { Release(); GC.SuppressFinalize(this); }
    }

    [Test]
    public void NotificationFailureDoesNotMisreportACommittedEditAsAnApplyFailure()
    {
        using var history = new UndoRedoHistory();
        var value = 0;
        history.Changed += () => throw new IOException("broken observer");
        history.Execute(new PropertyEdit<int>("edit", next => value = next, 0, 1));
        Assert.That(history.CanUndo, Is.True);
        Assert.That(history.LastNotificationError, Is.TypeOf<IOException>());
        history.Undo();
        Assert.That(value, Is.Zero);
    }

    private sealed record AsyncEdit(string Description, Func<ValueTask> RedoAction, Func<ValueTask> UndoAction) : IAsyncUndoableEdit
    {
        public ValueTask UndoAsync() => UndoAction();
        public ValueTask RedoAsync() => RedoAction();
    }

    private sealed record SizedEdit(Action RedoAction, Action UndoAction) : IUndoableEdit
    {
        public string Description => "sized";
        public long RetainedBytes => 8;
        public void Undo() => UndoAction();
        public void Redo() => RedoAction();
    }
}
