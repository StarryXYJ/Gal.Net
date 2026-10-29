using GalNet.Core.Runtime;

namespace GalNet.GameHost;

/// <summary>Serializes application commands and owns replacement and shutdown of game runs.</summary>
public sealed class GameSession(Func<CancellationToken, Task<IGameRun>> createRun) : IAsyncDisposable
{
    private readonly SemaphoreSlim _commands = new(1, 1);
    private readonly object _disposeGate = new();
    private IGameRun? _run;
    private bool _disposed;
    private Task? _disposeTask;

    public Task PrepareAsync(GameSnapshot? snapshot, CancellationToken cancellationToken = default) =>
        ExecuteAsync(token => PrepareCoreAsync(snapshot, token), cancellationToken);

    public Task PrepareNewAsync(IReadOnlyDictionary<string, object> initialSaveVariables, CancellationToken cancellationToken = default)
    {
        var copied = NewGameVariables.Capture(initialSaveVariables);
        return ExecuteAsync(token => PrepareCoreAsync(null, token, copied), cancellationToken);
    }

    public Task PrepareFromAsync(Func<CancellationToken, Task<GameSnapshot>> load,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async token => await PrepareCoreAsync(await load(token), token), cancellationToken);

    private async Task PrepareCoreAsync(GameSnapshot? snapshot, CancellationToken token,
        IReadOnlyDictionary<string, object>? initialSaveVariables = null)
    {
        if (snapshot is not null && _run is not null) await _run.ValidateSnapshotAsync(snapshot, token);
        await ReleaseRunAsync();
        var run = await createRun(token);
        _run = run;
        try
        {
            await run.PrepareAsync(snapshot, token);
            if (initialSaveVariables is { Count: > 0 })
            {
                if (run is not IParameterizedGameRun configurable)
                    throw new NotSupportedException("This game run does not accept initial save variables.");
                await configurable.InitializeSaveVariablesAsync(initialSaveVariables, token);
            }
        }
        catch (Exception error)
        {
            _run = null;
            try { await run.DisposeAsync(); }
            catch (Exception cleanupError) { throw new AggregateException(error, cleanupError); }
            throw;
        }
    }

    public Task BeginPreparedAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(token => RequireRun().StartPreparedAsync(token), cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(async _ => { if (_run is not null) await _run.StopAsync(); }, cancellationToken);

    public async Task<T> WithSnapshotAsync<T>(Func<GameSnapshot, CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        await _commands.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return await action(RequireRun().CreateSaveData(), cancellationToken);
        }
        finally { _commands.Release(); }
    }

    /// <summary>Stops and releases the current run before changing player-wide state.</summary>
    public Task ResetAsync(Func<CancellationToken, Task> reset, CancellationToken cancellationToken = default) =>
        ExecuteAsync(async token => { await ReleaseRunAsync(); await reset(token); }, cancellationToken);

    private IGameRun RequireRun() => _run ?? throw new InvalidOperationException("No game run has been prepared.");

    private async Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken token)
    {
        await _commands.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await action(token);
        }
        finally { _commands.Release(); }
    }

    private async Task ReleaseRunAsync()
    {
        var run = _run;
        _run = null;
        if (run is null) return;
        Exception? stopError = null;
        try { await run.StopAsync(); }
        catch (Exception error) { stopError = error; }
        try { await run.DisposeAsync(); }
        catch (Exception error) when (stopError is not null) { throw new AggregateException(stopError, error); }
        if (stopError is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(stopError).Throw();
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate) return new(_disposeTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        await _commands.WaitAsync();
        try { _disposed = true; await ReleaseRunAsync(); }
        finally { _commands.Release(); }
    }
}
