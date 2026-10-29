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
    private static readonly TimeSpan DebugRequestTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan StatusRequestTimeout = TimeSpan.FromSeconds(10);
    private CancellationTokenSource? _lifetime;
    private DebugListener? _listener;
    private DebugPeer? _peer;
    private Task? _connection;
    private Task? _launchMonitor;
    private TaskCompletionSource<bool>? _connected;
    private DebugLaunchDiagnostics? _diagnostics;
    private long _epoch;
    public event Action? Changed;
    public DebugHostStatus? Host { get; private set; }
    public GameDebugState? State { get; private set; }
    public string Status { get; private set; } = "Disconnected";
    public string? Error { get; private set; }
    public string? DiagnosticLogPath => _diagnostics?.FullPath;
    public DebugInvitation? Invitation => _listener?.Invitation;
    public bool IsConnected => _peer?.IsConnected == true;

    public DebugInvitation Listen(DebugIdentity identity)
    {
        if (_lifetime is not null) throw new InvalidOperationException("Disconnect the previous debug session first.");
        _diagnostics?.WriteLine($"Listening for project {identity.ProjectId}; content fingerprint={identity.ContentFingerprint}; contract fingerprint={identity.ContractFingerprint ?? "(none)"}.");
        _lifetime = new CancellationTokenSource();
        _listener = new DebugListener(identity);
        _diagnostics?.RegisterSecret(_listener.Invitation.Token);
        _connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Status = "Waiting for application";
        Error = null;
        var epoch = ++_epoch;
        _connection = AcceptAndPollAsync(_listener, epoch, _lifetime.Token);
        Changed?.Invoke();
        return _listener.Invitation;
    }

    public void BeginDiagnostics(string profileRoot)
    {
        _diagnostics?.Dispose();
        _diagnostics = DebugLaunchDiagnostics.TryCreate(profileRoot);
        _diagnostics?.WriteLine("Editor debug preparation started.");
    }

    public void RecordDiagnostic(string message) => _diagnostics?.WriteLine(message);

    public void Launch(GameDebugLaunchSettings settings, string contentRoot, string profileRoot)
    {
        var invitation = Invitation ?? throw new InvalidOperationException("Start listening before launching the App.");
        var lifetime = _lifetime!;
        var epoch = _epoch;
        _diagnostics ??= DebugLaunchDiagnostics.TryCreate(profileRoot);
        _launchMonitor = new DebugApplicationProcess(_diagnostics, () => !lifetime.IsCancellationRequested && epoch == _epoch,
            ReportLaunchFailure).Launch(settings, contentRoot, profileRoot, invitation, _connected!.Task);
    }
    private void ReportLaunchFailure(string message, string status)
    {
        Error = message;
        Status = status;
        Serilog.Log.Error("{DebugLaunchError}", message);
        Changed?.Invoke();
    }

    private string AddDiagnosticPath(string message) =>
        DiagnosticLogPath is { } path && !message.Contains(path, StringComparison.OrdinalIgnoreCase)
            ? $"{message}{Environment.NewLine}Diagnostics: {path}"
            : message;

    private async Task<JsonElement> RequestWithTimeoutAsync(DebugPeer peer, string method, object? payload, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        request.CancelAfter(timeout);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await peer.RequestAsync(method, payload, request.Token).ConfigureAwait(false);
            _diagnostics?.WriteLine($"Debug request '{method}' completed in {stopwatch.ElapsedMilliseconds} ms.");
            return result;
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            var message = $"The debug application did not respond to '{method}' within {timeout.TotalSeconds:0} seconds. Its operation may still be running; inspect its current state before retrying.";
            _diagnostics?.WriteLine($"Debug request '{method}' timed out after {stopwatch.ElapsedMilliseconds} ms.");
            throw new TimeoutException(message);
        }
        catch (Exception error)
        {
            _diagnostics?.WriteLine($"Debug request '{method}' failed after {stopwatch.ElapsedMilliseconds} ms: {error.Message}");
            throw;
        }
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
        return await RequestWithTimeoutAsync(peer, method, payload, DebugRequestTimeout, cancellationToken).ConfigureAwait(false);
    }
    private async Task AcceptAndPollAsync(DebugListener listener, long epoch, CancellationToken token)
    {
        try
        {
            var peer = await listener.AcceptAsync(token).ConfigureAwait(false);
            if (epoch != _epoch) { await peer.DisposeAsync().ConfigureAwait(false); return; }
            _peer = peer;
            _connected?.TrySetResult(true);
            _diagnostics?.WriteLine("Authenticated debug connection established.");
            Error = null;
            while (peer.IsConnected)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    Host = (await RequestWithTimeoutAsync(peer, "status", null, StatusRequestTimeout, token).ConfigureAwait(false)).Deserialize<DebugHostStatus>(Json);
                    if (Host?.Run is { } run && run.Capabilities.Contains("inspect"))
                    {
                        try
                        {
                            var command = new DebugCommand("inspect", run.RunId, run.Generation, JsonSerializer.SerializeToElement(new { }, Json));
                            State = (await RequestWithTimeoutAsync(peer, "inspect", command, StatusRequestTimeout, token).ConfigureAwait(false)).Deserialize<GameDebugState>(Json);
                        }
                        catch (InvalidOperationException error) { State = null; Error = error.Message; }
                    }
                    else State = null;
                    Error = null;
                    Status = State is null ? "Application connected" : State.Paused ? "Paused" : State.State.ToString();
                }
                catch (TimeoutException error)
                {
                    Error = AddDiagnosticPath(error.Message);
                    Status = "Application not responding";
                    _diagnostics?.WriteLine(error.Message);
                }
                Changed?.Invoke();
                await Task.Delay(150, token).ConfigureAwait(false);
            }
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException or System.Net.Sockets.SocketException or TimeoutException)
        {
            if (!token.IsCancellationRequested)
            {
                Error = AddDiagnosticPath(error.Message);
                _diagnostics?.WriteLine($"Debug connection failed: {error}");
            }
        }
        finally
        {
            if (epoch == _epoch) { Host = null; State = null; Status = "Disconnected"; Changed?.Invoke(); }
        }
    }
    public async Task DisconnectAsync()
    {
        var lifetime = _lifetime;
        if (lifetime is null) return;
        _diagnostics?.WriteLine("Editor debug session disconnected. The external App process was not terminated.");
        var launchMonitor = _launchMonitor;
        var diagnostics = _diagnostics;
        ++_epoch;
        await lifetime.CancelAsync().ConfigureAwait(false);
        _listener?.Dispose();
        if (_peer is not null) await _peer.DisposeAsync().ConfigureAwait(false);
        if (_connection is not null) await _connection.ConfigureAwait(false);
        _launchMonitor = null;
        _connected = null;
        _peer = null;
        _listener = null;
        _lifetime = null;
        lifetime.Dispose();
        _diagnostics = null;
        if (diagnostics is not null)
        {
            if (launchMonitor is null)
            {
                diagnostics.Dispose();
            }
            else
            {
                _ = launchMonitor.ContinueWith(
                    completed =>
                    {
                        if (completed.Exception is { } error)
                            Serilog.Log.Warning(error.Flatten(), "Debug application output monitor ended unexpectedly");
                        diagnostics.Dispose();
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
        Host = null;
        State = null;
        Status = "Disconnected";
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
