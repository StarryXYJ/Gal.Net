using System;
using System.Threading;
using System.Threading.Tasks;
using GalNet.Editor.Services.Interfaces;
using Serilog;

namespace GalNet.Editor.Services;

/// <summary>Project-scoped save queue. At most one write is active and a newer auto-save replaces an older pending one.</summary>
public sealed class ProjectSaveScheduler : IProjectSaveScheduler, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private CancellationTokenSource? _pendingSave;
    private bool _disposed;

    public void Schedule(Func<Task> save)
    {
        ArgumentNullException.ThrowIfNull(save);
        CancellationTokenSource cancellation;
        lock (_sync)
        {
            ThrowIfDisposed();
            _pendingSave?.Cancel();
            _pendingSave?.Dispose();
            cancellation = _pendingSave = new CancellationTokenSource();
        }

        _ = RunDelayedAsync(save, cancellation);
    }

    public Task SaveNowAsync(Func<Task> save)
    {
        ArgumentNullException.ThrowIfNull(save);
        lock (_sync)
        {
            ThrowIfDisposed();
            _pendingSave?.Cancel();
            _pendingSave?.Dispose();
            _pendingSave = null;
        }

        return RunExclusiveAsync(save, CancellationToken.None);
    }

    private async Task RunDelayedAsync(Func<Task> save, CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellation.Token);
            await RunExclusiveAsync(save, cancellation.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            Log.Error(exception, "Auto-save failed");
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_pendingSave, cancellation))
                    _pendingSave = null;
            }
            cancellation.Dispose();
        }
    }

    private async Task RunExclusiveAsync(Func<Task> save, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await save();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            _pendingSave?.Cancel();
            _pendingSave?.Dispose();
            _pendingSave = null;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ProjectSaveScheduler));
    }
}
