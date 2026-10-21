using System.Diagnostics;
using System.Text.Json;
using GalNet.Debugging;
using GalNet.Runtime.Engine;

namespace GalNet.Editor.Shared.Services;

public sealed record GameDebugLaunchSettings(string Executable, string[] Arguments, string? WorkingDirectory = null);

/// <summary>Project-owned connection; contains no UI, engine object references or process termination policy.</summary>
public sealed class ExternalGameDebugClient : IAsyncDisposable, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private CancellationTokenSource? _lifetime;
    private DebugListener? _listener;
    private DebugPeer? _peer;
    private Task? _connection;
    private long _epoch;
    public event Action? Changed;
    public DebugHostStatus? Host { get; private set; }
    public GameDebugState? State { get; private set; }
    public string Status { get; private set; } = "Disconnected";
    public string? Error { get; private set; }
    public DebugInvitation? Invitation => _listener?.Invitation;
    public bool IsConnected => _peer?.IsConnected == true;

    public DebugInvitation Listen(DebugIdentity identity)
    {
        if (_lifetime is not null) throw new InvalidOperationException("Disconnect the previous debug session first.");
        _lifetime = new CancellationTokenSource();
        _listener = new DebugListener(identity);
        Status = "Waiting for application";
        Error = null;
        var epoch = ++_epoch;
        _connection = AcceptAndPollAsync(_listener, _lifetime.Token, epoch);
        Changed?.Invoke();
        return _listener.Invitation;
    }
    public void Launch(GameDebugLaunchSettings settings, string contentRoot, string profileRoot)
    {
        var invitation = Invitation ?? throw new InvalidOperationException("Start listening before launching the App.");
        if (!File.Exists(settings.Executable)) throw new FileNotFoundException("Debug application was not found.", settings.Executable);
        var start = new ProcessStartInfo(settings.Executable)
        {
            UseShellExecute = false,
            WorkingDirectory = settings.WorkingDirectory ?? Path.GetDirectoryName(settings.Executable)!
        };
        foreach (var argument in settings.Arguments) start.ArgumentList.Add(argument);
        foreach (var argument in new[] { contentRoot, "--profile", profileRoot, "--debug-port", invitation.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--debug-token", invitation.Token, "--debug-session", invitation.SessionId, "--debug-project", invitation.Identity.ProjectId })
            start.ArgumentList.Add(argument);
        Process.Start(start)?.Dispose();
    }
    public async Task<JsonElement> SendAsync(string method, object? arguments = null, string? expectedRun = null, long? expectedGeneration = null,
        CancellationToken cancellationToken = default)
    {
        var peer = _peer ?? throw new InvalidOperationException("No debug application is connected.");
        object? payload = arguments;
        if (method is not ("start" or "status"))
        {
            var run = Host?.Run ?? throw new InvalidOperationException("No game instance is attached.");
            if ((expectedRun is not null && expectedRun != run.RunId) || (expectedGeneration is not null && expectedGeneration != run.Generation))
                throw new InvalidOperationException("The debug execution has changed. Refresh the draft.");
            payload = new DebugCommand(method, expectedRun ?? run.RunId, expectedGeneration ?? run.Generation, JsonSerializer.SerializeToElement(arguments, Json));
        }
        return await peer.RequestAsync(method, payload, cancellationToken).ConfigureAwait(false);
    }
    private async Task AcceptAndPollAsync(DebugListener listener, CancellationToken token, long epoch)
    {
        try
        {
            var peer = await listener.AcceptAsync(token).ConfigureAwait(false);
            if (epoch != _epoch) { await peer.DisposeAsync().ConfigureAwait(false); return; }
            _peer = peer;
            while (peer.IsConnected)
            {
                token.ThrowIfCancellationRequested();
                Host = (await peer.RequestAsync("status", cancellationToken: token).ConfigureAwait(false)).Deserialize<DebugHostStatus>(Json);
                if (Host?.Run is { } run)
                {
                    try
                    {
                        var command = new DebugCommand("inspect", run.RunId, run.Generation, JsonSerializer.SerializeToElement(new { }, Json));
                        State = (await peer.RequestAsync("inspect", command, token).ConfigureAwait(false)).Deserialize<GameDebugState>(Json);
                        Error = null;
                    }
                    catch (InvalidOperationException error) { State = null; Error = error.Message; }
                }
                else State = null;
                Status = State is null ? "Application connected" : State.Paused ? "Paused" : State.State.ToString();
                Changed?.Invoke();
                await Task.Delay(150, token).ConfigureAwait(false);
            }
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException or System.Net.Sockets.SocketException)
        { if (!token.IsCancellationRequested) Error = error.Message; }
        finally
        {
            if (epoch == _epoch) { Host = null; State = null; Status = "Disconnected"; Changed?.Invoke(); }
        }
    }
    public async Task DisconnectAsync()
    {
        var lifetime = _lifetime;
        if (lifetime is null) return;
        await lifetime.CancelAsync().ConfigureAwait(false);
        _listener?.Dispose();
        if (_peer is not null) await _peer.DisposeAsync().ConfigureAwait(false);
        if (_connection is not null) await _connection.ConfigureAwait(false);
        _peer = null;
        _listener = null;
        _lifetime = null;
        lifetime.Dispose();
        Host = null;
        State = null;
        Changed?.Invoke();
    }
    public ValueTask DisposeAsync() => new(DisconnectAsync());
    public void Dispose() => DisconnectAsync().GetAwaiter().GetResult();
    public static GameDebugLaunchSettings? LoadSettings(string projectRoot)
    {
        var path = SettingsPath(projectRoot);
        return File.Exists(path) ? JsonSerializer.Deserialize<GameDebugLaunchSettings>(File.ReadAllText(path), Json) : null;
    }
    public static void SaveSettings(string projectRoot, GameDebugLaunchSettings settings)
    {
        var path = SettingsPath(projectRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(settings, Json));
    }
    private static string SettingsPath(string root) => Path.Combine(root, ".galnet", "editor", "debug-launch.json");
}
