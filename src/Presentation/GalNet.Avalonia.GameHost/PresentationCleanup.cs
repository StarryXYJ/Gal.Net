namespace GalNet.Avalonia.GameHost;

/// <summary>Attempts explicitly ordered releases and preserves every failure.</summary>
internal sealed class PresentationCleanup
{
    private readonly List<Exception> _errors = [];
    public void Run(Action release)
    {
        try { release(); }
        catch (Exception error) { _errors.Add(error); }
    }
    public async Task RunAsync(Func<ValueTask> release)
    {
        try { await release(); }
        catch (Exception error) { _errors.Add(error); }
    }
    public void ThrowIfFailed(Exception? original = null)
    {
        if (_errors.Count == 0) return;
        if (original is not null) _errors.Insert(0, original);
        throw new AggregateException(_errors);
    }
}
