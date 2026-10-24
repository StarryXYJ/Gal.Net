using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace GalNet.Debugging;

/// <summary>Optional App service. The developer owns the App and run; this service only borrows bindings.</summary>
public sealed class GameDebugHost(Func<CancellationToken, Task>? startGame = null) : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _commands = new(1, 1);
    private IDebugGameRun? _run;
    private DebugPeer? _peer;
    private Task _disconnection = Task.CompletedTask;
    private string _sessionId = "";
    private bool _disposed;

    public IDisposable AttachRun(IDebugGameRun run)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_run is not null) throw new InvalidOperationException("A debug run is already attached.");
            _run = run;
        }
        return new Binding(this, run);
    }
    public async Task ConnectAsync(DebugInvitation invitation, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_peer?.IsConnected == true) throw new InvalidOperationException("Disconnect before reconnecting.");
        if (_peer is not null) await DisconnectAsync().ConfigureAwait(false);
        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, invitation.Port, cancellationToken).ConfigureAwait(false);
            var stream = client.GetStream();
            await DebugWire.WriteAsync(stream, new DebugHello(1, invitation.Token, invitation.SessionId, invitation.Identity), cancellationToken).ConfigureAwait(false);
            if (!await DebugWire.ReadAsync<bool>(stream, cancellationToken).ConfigureAwait(false)) throw new InvalidDataException("Debug handshake failed.");
            _sessionId = invitation.SessionId;
            _peer = new DebugPeer(stream, HandleAsync);
            _disconnection = ObserveDisconnectAsync(_peer);
        }
        catch { client.Dispose(); throw; }
    }
    private async Task<object?> HandleAsync(string method, JsonElement payload, CancellationToken token)
    {
        IDebugGameRun? run;
        lock (_sync) run = _run;
        if (method == "status") return new DebugHostStatus(_sessionId, startGame is not null, run?.Info);
        if (method is "cancel" or "stop")
        {
            var cancel = payload.Deserialize<DebugCommand>(DebugWire.Json) ?? throw new InvalidDataException("Missing debug command.");
            if (run is null || cancel.Method != method || cancel.RunId != run.Info.RunId ||
                (cancel.Generation != run.Info.Generation && cancel.Generation != run.Info.OperationGeneration))
                throw new InvalidOperationException("The debug run has changed. Refresh before submitting.");
            var result = await run.ExecuteAsync(cancel, token).ConfigureAwait(false);
            if (method == "stop") { lock (_sync) if (ReferenceEquals(_run, run)) _run = null; }
            return result;
        }
        await _commands.WaitAsync(token).ConfigureAwait(false);
        try
        {
            lock (_sync) run = _run;
            if (method == "start")
            {
                if (startGame is null) throw new NotSupportedException("This App does not register a start-game callback.");
                if (run is not null) throw new InvalidOperationException("Stop the current game before starting another.");
                await startGame(token).ConfigureAwait(false);
                return null;
            }
            var command = payload.Deserialize<DebugCommand>(DebugWire.Json) ?? throw new InvalidDataException("Missing debug command.");
            if (run is null || command.RunId != run.Info.RunId || command.Generation != run.Info.Generation)
                throw new InvalidOperationException("The debug run has changed. Refresh before submitting.");
            if (method != command.Method) throw new InvalidDataException("Debug command method mismatch.");
            var result = await run.ExecuteAsync(command, token).ConfigureAwait(false);
            if (method == "stop") { lock (_sync) if (ReferenceEquals(_run, run)) _run = null; }
            return result;
        }
        finally { _commands.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        lock (_sync) { _disposed = true; _run = null; }
        await DisconnectAsync().ConfigureAwait(false);
    }
    public async Task DisconnectAsync()
    {
        var peer = _peer;
        _peer = null;
        if (peer is not null) await peer.DisposeAsync().ConfigureAwait(false);
        await _disconnection.ConfigureAwait(false);
    }
    private async Task ObserveDisconnectAsync(DebugPeer peer)
    {
        await peer.Completion.ConfigureAwait(false);
        await _commands.WaitAsync().ConfigureAwait(false);
        try
        {
            IDebugGameRun? run;
            lock (_sync) run = _run;
            if (run is not null) await run.OnDisconnectedAsync().ConfigureAwait(false);
        }
        finally { _commands.Release(); }
    }
    private sealed class Binding(GameDebugHost owner, IDebugGameRun run) : IDisposable
    {
        public void Dispose() { lock (owner._sync) if (ReferenceEquals(owner._run, run)) owner._run = null; }
    }
}
