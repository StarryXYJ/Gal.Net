namespace GalNet.Sample.Headless;

/// <summary>Serializes prompts and preserves a pending read when its current waiter is cancelled.</summary>
internal sealed class ConsoleInput(TextReader reader)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Task<string?>? _pendingRead;

    public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Some synchronized TextReader implementations perform ReadLineAsync synchronously.
            var read = _pendingRead ??= Task.Run(reader.ReadLineAsync, CancellationToken.None);
            try
            {
                var line = await read.WaitAsync(cancellationToken);
                _pendingRead = null;
                return line;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // ReadLine itself may not be cancellable. The next prompt owns its result.
                throw;
            }
            catch
            {
                _pendingRead = null;
                throw;
            }
        }
        finally { _gate.Release(); }
    }
}
