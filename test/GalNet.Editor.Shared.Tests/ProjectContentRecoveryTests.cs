using System.Collections.Concurrent;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Shared.Services;

namespace GeneralTest.Editor;

public sealed partial class ProjectContentSessionTests
{
    [Test]
    public async Task PeriodicRecoveryReleasesCaptureGuardCoalescesTicksAndCloseWaitsForLatestContent()
    {
        _session.Dispose();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var guardReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var snapshots = new ConcurrentQueue<string>();
        var captures = 0;
        var store = new ProjectRecoveryStore(_root, new());
        var writer = new ProjectRecoveryWriter(snapshot =>
        {
            snapshots.Enqueue(snapshot.Graph.Name);
            if (snapshots.Count == 1)
            {
                entered.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test writer was not released.");
            }
            store.Capture(snapshot);
        });
        _session = new(_documents, _repository, new() { RecoveryInterval = TimeSpan.FromMilliseconds(10) }, writer);
        using var registration = _session.RegisterCaptureGuard((_, _) =>
        {
            Interlocked.Increment(ref captures);
            return Task.FromResult<IDisposable>(new RecoveryLease(() => guardReleased.TrySetResult()));
        });
        _session.Open(_project);
        _documents.CurrentDocument.Name = "captured before write";
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await guardReleased.Task.WaitAsync(TimeSpan.FromSeconds(5));
            _documents.CurrentDocument.Name = "edited while writing";
            await Task.Delay(60); // Several ticks elapse; no additional live snapshot should be captured.
            Assert.That(Volatile.Read(ref captures), Is.EqualTo(1));
            var closing = _project.DisposeAsync().AsTask();
            Assert.That(closing.IsCompleted, Is.False, "Closing must await the blocked persistence.");
            release.Set();
            await closing.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(snapshots.ToArray(), Is.EqualTo(new[] { "captured before write", "edited while writing" }));
            Assert.That(_session.LastRecoveryError, Is.Null);
            var paths = Directory.GetFiles(Path.Combine(_root, ".galnet/editor/recovery"), "*.json");
            Assert.That(paths.Select(path => ProjectRecoveryStore.Read(path).Graph.Name), Does.Contain("edited while writing"));
        }
        finally { release.Set(); }
    }

    [Test]
    public async Task CriticalRecoveryWaitsForEarlierWriteAndUsesDetachedContent()
    {
        _session.Dispose();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var snapshots = new ConcurrentQueue<EditorProjectDocument>();
        _session = new(_documents, _repository, new(), new(snapshot =>
        {
            if (snapshots.IsEmpty)
            {
                entered.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            }
            snapshots.Enqueue(snapshot);
        }));
        _session.Open(_project);
        _documents.CurrentDocument.Name = "first";
        var first = _session.CaptureRecoveryAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            _documents.CurrentDocument.Name = "second";
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var critical = Task.Run(() => { started.SetResult(); _session.ReportError(new IOException("author failure")); });
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(critical.IsCompleted, Is.False);
            release.Set();
            await Task.WhenAll(first, critical).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(snapshots.Select(snapshot => snapshot.Graph.Name), Is.EqualTo(new[] { "first", "second" }));
            Assert.That(_session.IsDirty, Is.True, "Recovery must not advance the saved baseline.");
        }
        finally { release.Set(); }
    }

    [Test]
    public async Task DisposeDrainsWorkerWithoutPostingToTheCallerContext()
    {
        _session.Dispose();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var written = false;
        _session = new(_documents, _repository, new(), new(_ =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            written = true;
        }));
        _session.Open(_project);
        var writing = _session.CaptureRecoveryAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var disposing = Task.Run(() =>
            {
                SynchronizationContext.SetSynchronizationContext(new RejectPosts());
                try { started.SetResult(); _session.Dispose(); }
                finally { SynchronizationContext.SetSynchronizationContext(null); }
            });
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(disposing.IsCompleted, Is.False);
            release.Set();
            await Task.WhenAll(writing, disposing).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(written, Is.True);
            Assert.That(_session.CaptureRecoveryAsync().IsCompletedSuccessfully, Is.True);
        }
        finally { release.Set(); }
    }

    [Test]
    public async Task SaveAndLaterEditsAreIndependentOfAnEarlierRecoveryWrite()
    {
        _session.Dispose();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? recoveredName = null;
        _session = new(_documents, _repository, new(), new(snapshot =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            recoveredName = snapshot.Graph.Name;
        }));
        _session.Open(_project);
        _documents.CurrentDocument.Name = "recovery version";
        var writing = _session.CaptureRecoveryAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            _documents.CurrentDocument.Name = "saved version";
            await _session.SaveAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(_session.IsDirty, Is.False);
            Assert.That(writing.IsCompleted, Is.False);
            _documents.CurrentDocument.Name = "later edit";
            release.Set();
            await writing.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(recoveredName, Is.EqualTo("recovery version"));
            Assert.That(_session.LastRecoveryError, Is.Null);
            Assert.That(_session.IsDirty, Is.True);
            _documents.CurrentDocument.Name = "saved version";
            Assert.That(_session.IsDirty, Is.False, "Late recovery completion must not replace the save baseline.");
        }
        finally { release.Set(); }
    }

    [Test]
    public async Task RecoveryWriteFailureIsObservableAndNextWriteCanSucceed()
    {
        var directory = Path.Combine(_root, ".galnet/editor/recovery");
        Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
        File.WriteAllText(directory, "blocks directory creation");
        try
        {
            await _session.CaptureRecoveryAsync();
            Assert.That(_session.LastRecoveryError, Is.InstanceOf<IOException>());
        }
        finally { File.Delete(directory); }
        await _session.CaptureRecoveryAsync();
        Assert.That(_session.LastRecoveryError, Is.Null);
        Assert.That(Directory.GetFiles(directory, "*.json"), Has.Length.EqualTo(1));
    }

    private sealed class RecoveryLease(Action release) : IDisposable { public void Dispose() => release(); }
    private sealed class RejectPosts : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) => throw new InvalidOperationException("Worker must not need the caller context.");
    }
}

public sealed class ProjectRecoveryWriterTests
{
    [Test]
    public async Task WritesStayOrderedAfterFailureAndStopRejectsNewWork()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var seen = new ConcurrentQueue<string>();
        var failure = new IOException("first write fails");
        ProjectRecoveryWriter? writer = null;
        writer = new(snapshot =>
        {
            seen.Enqueue(snapshot.Graph.Name);
            if (snapshot.Graph.Name == "first")
            {
                entered.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
                throw failure;
            }
            Assert.That(writer!.LastError, Is.SameAs(failure));
        });
        try
        {
            var first = writer.WriteAsync(new() { Graph = new() { Name = "first" } });
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var second = writer.WriteAsync(new() { Graph = new() { Name = "second" } });
            var stopped = writer.StopAsync();
            Assert.That(stopped.IsCompleted, Is.False);
            Assert.Throws<ObjectDisposedException>(() => writer.WriteAsync(new()));
            release.Set();
            await Task.WhenAll(first, second, stopped).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(seen.ToArray(), Is.EqualTo(new[] { "first", "second" }));
            Assert.That(writer.LastError, Is.Null);
        }
        finally { release.Set(); writer.Dispose(); }
    }
}
