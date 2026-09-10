namespace GalNet.Presentation.Abstractions.Runtime;

/// <summary>Owns one cancellable game flow. Hosts serialize start/stop decisions themselves.</summary>
public sealed class GameRunCoordinator : IDisposable
{
    private CancellationTokenSource? _cancellation;
    private Task? _run;

    public void Start(Func<CancellationToken, Task> run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (_run is not null)
            throw new InvalidOperationException("A game flow is already active. Stop it before starting another.");

        var cancellation = new CancellationTokenSource();
        try
        {
            _run = run(cancellation.Token);
            _cancellation = cancellation;
        }
        catch
        {
            cancellation.Dispose();
            throw;
        }
    }

    public Task WaitAsync(CancellationToken cancellationToken = default) =>
        _run?.WaitAsync(cancellationToken) ?? Task.CompletedTask;

    public async Task StopAsync()
    {
        var cancellation = _cancellation;
        var run = _run;
        _cancellation = null;
        _run = null;

        cancellation?.Cancel();
        try
        {
            if (run is not null) await run;
        }
        catch (OperationCanceledException) { }
        finally
        {
            cancellation?.Dispose();
        }
    }

    public void Dispose() => StopAsync().GetAwaiter().GetResult();
}
