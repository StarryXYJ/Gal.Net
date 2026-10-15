using GalNet.Core.Primitives;
using GalNet.Core.Runtime;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Presentation.Abstractions.Execution;

public sealed class StateRestoreContext(IGameRuntime runtime, Action<PrimitiveInstance> track, Action<Func<Task>> own, CancellationToken token)
{
    public IGameRuntime Runtime { get; } = runtime;
    public CancellationToken CancellationToken { get; } = token;
    public void Start(PrimitiveInstance instance) { track(instance); instance.Dispatch(); }
    public void Own(Func<Task> cleanup) => own(cleanup);
}

internal sealed record StateRestorer(string Id, string[] After, Func<StateRestoreContext, Task> Restore);

/// <summary>Owns module restoration continuations without knowing their state types.</summary>
public sealed class StateRestoreSession(CompositeGameView view) : IAsyncDisposable
{
    private readonly List<PrimitiveInstance> _continuations = [];
    private readonly List<Func<Task>> _cleanups = [];
    private Task? _suspendTask;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _disposeSync = new();
    private Task? _disposeTask;
    private volatile bool _disposed;
    public async Task RestoreAsync(IGameRuntime runtime, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await SuspendCoreAsync(cancellationToken).ConfigureAwait(false);
            runtime.State.Configure(view.StateCatalog);
            var context = new StateRestoreContext(runtime, _continuations.Add, _cleanups.Add, cancellationToken);
            foreach (var restorer in view.StateRestorers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await restorer.Restore(context).ConfigureAwait(false);
            }
        }
        finally { _gate.Release(); }
    }
    public async Task SuspendAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await SuspendCoreAsync(cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }
    private async Task SuspendCoreAsync(CancellationToken cancellationToken)
    {
        var drain = _suspendTask ??= DrainAsync();
        try { await drain.WaitAsync(cancellationToken).ConfigureAwait(false); }
        finally
        {
            // A canceled wait retains ownership until the actual drain completes.
            if (drain.IsCompleted)
            {
                _continuations.Clear();
                _cleanups.Clear();
                _suspendTask = null;
            }
        }
    }
    private async Task DrainAsync()
    {
        List<Exception>? errors = null;
        foreach (var instance in _continuations)
            try { instance.ApplyPlaybackMode(PrimitiveInvocationMode.CatchUp); }
            catch (Exception error) { (errors ??= []).Add(error); }
        try { await Task.WhenAll(_continuations.Select(instance => instance.PresentationCompletion)).ConfigureAwait(false); }
        catch (Exception error) { (errors ??= []).Add(error); }
        foreach (var cleanup in _cleanups.AsEnumerable().Reverse())
            try { await cleanup().ConfigureAwait(false); }
            catch (Exception error) { (errors ??= []).Add(error); }
        if (errors is not null) throw new AggregateException(errors);
    }
    public ValueTask DisposeAsync()
    {
        lock (_disposeSync) { _disposed = true; return new(_disposeTask ??= DisposeCoreAsync()); }
    }
    private async Task DisposeCoreAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { await SuspendCoreAsync(CancellationToken.None).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }
}
