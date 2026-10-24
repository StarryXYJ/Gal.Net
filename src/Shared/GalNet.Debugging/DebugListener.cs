using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace GalNet.Debugging;

/// <summary>Editor-owned single-session loopback listener. Failed authentication never binds a run.</summary>
public sealed class DebugListener : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    public DebugListener(DebugIdentity identity)
    {
        _listener.Start();
        Invitation = new(((IPEndPoint)_listener.LocalEndpoint).Port, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), Guid.NewGuid().ToString("N"), identity);
    }
    public DebugInvitation Invitation { get; }
    public async Task<DebugPeer> AcceptAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using var handshake = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                handshake.CancelAfter(TimeSpan.FromSeconds(10));
                var stream = client.GetStream();
                var hello = await DebugWire.ReadAsync<DebugHello>(stream, handshake.Token).ConfigureAwait(false);
                if (hello.Version != 1 || hello.Token != Invitation.Token || hello.SessionId != Invitation.SessionId || hello.Identity != Invitation.Identity)
                    throw new InvalidDataException("Debug identity or protocol mismatch.");
                await DebugWire.WriteAsync(stream, true, handshake.Token).ConfigureAwait(false);
                return new DebugPeer(stream, (_, _, _) => Task.FromException<object?>(new NotSupportedException("Editor does not accept runtime commands.")));
            }
            catch (Exception error) when (error is IOException or OperationCanceledException or System.Text.Json.JsonException or InvalidDataException)
            {
                client.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }
    public void Dispose() => _listener.Stop();
}
