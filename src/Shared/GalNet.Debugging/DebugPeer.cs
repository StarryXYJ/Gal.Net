using System.Collections.Concurrent;
using System.Text.Json;

namespace GalNet.Debugging;

/// <summary>Bounded duplex request transport; disconnect cancels in-flight host work.</summary>
public sealed class DebugPeer : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _writes = new(1, 1);
    private readonly SemaphoreSlim _requests = new(16, 16);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly ConcurrentDictionary<string, Task> _handlers = new();
    private readonly Func<string, JsonElement, CancellationToken, Task<object?>> _handler;
    private readonly Task _reader;

    internal DebugPeer(Stream stream, Func<string, JsonElement, CancellationToken, Task<object?>> handler)
    {
        _stream = stream;
        _handler = handler;
        _reader = ReadLoopAsync();
    }
    public Task Completion => _reader;
    public bool IsConnected => !_lifetime.IsCancellationRequested;

    public async Task<JsonElement> RequestAsync(string method, object? arguments = null, CancellationToken cancellationToken = default)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _requests.WaitAsync(cancellation.Token).ConfigureAwait(false);
        var id = Guid.NewGuid().ToString("N");
        var pending = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = pending;
        try
        {
            await SendAsync(new DebugMessage("request", id, method, DebugWire.Element(arguments)), cancellation.Token).ConfigureAwait(false);
            return await pending.Task.WaitAsync(cancellation.Token).ConfigureAwait(false);
        }
        finally { _pending.TryRemove(id, out _); _requests.Release(); }
    }
    private async Task SendAsync(DebugMessage message, CancellationToken token)
    {
        await _writes.WaitAsync(token).ConfigureAwait(false);
        try { await DebugWire.WriteAsync(_stream, message, token).ConfigureAwait(false); }
        finally { _writes.Release(); }
    }
    private async Task ReadLoopAsync()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                var message = await DebugWire.ReadAsync<DebugMessage>(_stream, _lifetime.Token).ConfigureAwait(false);
                if (message.Kind == "response")
                {
                    if (_pending.TryRemove(message.Id, out var pending))
                    {
                        if (message.Error is { } error) pending.TrySetException(new InvalidOperationException(error));
                        else pending.TrySetResult(message.Payload.Clone());
                    }
                }
                else if (message.Kind == "request")
                {
                    foreach (var item in _handlers.Where(pair => pair.Value.IsCompleted)) _handlers.TryRemove(item.Key, out _);
                    if (_handlers.Count >= 16 || _handlers.ContainsKey(message.Id)) throw new InvalidDataException("Too many debug requests.");
                    _handlers[message.Id] = HandleAsync(message);
                }
                else throw new InvalidDataException("Unknown debug message kind.");
            }
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException or JsonException or InvalidDataException) { }
        finally
        {
            await _lifetime.CancelAsync().ConfigureAwait(false);
            foreach (var pending in _pending.Values) pending.TrySetException(new IOException("Debug connection closed."));
        }
    }
    private async Task HandleAsync(DebugMessage request)
    {
        DebugMessage response;
        try
        {
            var result = await _handler(request.Method ?? "", request.Payload, _lifetime.Token).ConfigureAwait(false);
            response = new("response", request.Id, null, DebugWire.Element(result));
        }
        catch (Exception error) { response = new("response", request.Id, null, DebugWire.Element(null), error.Message); }
        try { await SendAsync(response, _lifetime.Token).ConfigureAwait(false); }
        catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException) { }
    }
    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync().ConfigureAwait(false);
        _stream.Dispose();
        await _reader.ConfigureAwait(false);
        await Task.WhenAll(_handlers.Values).ConfigureAwait(false);
    }
}
