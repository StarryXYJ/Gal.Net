using System.Diagnostics;
using System.Text;
using System.Text.Json;
using GalNet.Debugging;
using GalNet.Runtime.Engine;

namespace GalNet.Editor.Shared.Services;

public sealed record GameDebugLaunchSettings(string Executable, string[] Arguments, string? WorkingDirectory = null);

/// <summary>Project-owned connection; contains no UI, engine object references or process termination policy.</summary>
public sealed class ExternalGameDebugClient : IAsyncDisposable, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan LaunchConnectionTimeout = TimeSpan.FromSeconds(30);
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
        _diagnostics ??= DebugLaunchDiagnostics.TryCreate(profileRoot);
        if (!File.Exists(settings.Executable))
        {
            var message = AddDiagnosticPath($"Debug application was not found: {settings.Executable}");
            _diagnostics?.WriteLine(message);
            throw new FileNotFoundException(message, settings.Executable);
        }
        _diagnostics?.WriteLine("Debug App launch requested.");
        _diagnostics?.WriteLine($"Executable: {settings.Executable}");
        _diagnostics?.WriteLine($"Working directory: {settings.WorkingDirectory ?? Path.GetDirectoryName(settings.Executable)}");
        _diagnostics?.WriteLine($"Content root: {contentRoot}");
        _diagnostics?.WriteLine($"Profile root: {profileRoot}");
        _diagnostics?.WriteLine($"Project ID: {invitation.Identity.ProjectId}");
        _diagnostics?.WriteLine($"Content fingerprint: {invitation.Identity.ContentFingerprint}");
        _diagnostics?.WriteLine($"Contract fingerprint: {invitation.Identity.ContractFingerprint ?? "(none)"}");
        _diagnostics?.WriteLine($"Additional configured argument count: {settings.Arguments.Length}; argument values are omitted.");
        _diagnostics?.WriteLine($"Connection timeout: {LaunchConnectionTimeout.TotalSeconds:0} seconds.");
        var start = new ProcessStartInfo(settings.Executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = settings.WorkingDirectory ?? Path.GetDirectoryName(settings.Executable)!
        };
        foreach (var argument in settings.Arguments) start.ArgumentList.Add(argument);
        foreach (var argument in new[] { contentRoot, "--profile", profileRoot, "--debug-port", invitation.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--debug-token", invitation.Token, "--debug-session", invitation.SessionId, "--debug-project", invitation.Identity.ProjectId })
            start.ArgumentList.Add(argument);
        if (invitation.Identity.ContractFingerprint is { } contract) { start.ArgumentList.Add("--debug-contract"); start.ArgumentList.Add(contract); }
        Process process;
        try { process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the debug application."); }
        catch (Exception error)
        {
            _diagnostics?.WriteLine($"Process start failed: {error}");
            throw new InvalidOperationException(AddDiagnosticPath(error.Message), error);
        }
        Serilog.Log.Information("Started debug application {Executable}, process {ProcessId}", settings.Executable, process.Id);
        _diagnostics?.WriteLine($"Process started. PID={process.Id}.");
        _diagnostics?.WriteLine("Waiting for the App to complete the authenticated debug connection.");
        _launchMonitor = MonitorLaunchAsync(process, _epoch, _connected!.Task, invitation.Token, _diagnostics, _lifetime!.Token);
    }
    private async Task MonitorLaunchAsync(Process process, long epoch, Task connected, string secretToken, DebugLaunchDiagnostics? diagnostics, CancellationToken token)
    {
        using (process)
        {
            try
            {
                var stdoutTail = new BoundedOutputTail(secretToken);
                var stderrTail = new BoundedOutputTail(secretToken);
                // Keep draining the redirected pipes until the child exits, even after the
                // Editor disconnects. A child can outlive its debug session and must not block
                // while writing to a full stdout/stderr pipe.
                var stdout = CaptureOutputAsync(process.StandardOutput, "stdout", process.Id, secretToken, stdoutTail, diagnostics);
                var stderr = CaptureOutputAsync(process.StandardError, "stderr", process.Id, secretToken, stderrTail, diagnostics);
                var exit = process.WaitForExitAsync(CancellationToken.None);
                var timeout = Task.Delay(LaunchConnectionTimeout, CancellationToken.None);
                var first = await Task.WhenAny(connected, exit, timeout).ConfigureAwait(false);
                if (first == timeout && !token.IsCancellationRequested && epoch == _epoch && !connected.IsCompleted && !process.HasExited)
                {
                    var details = FormatOutputTails(stdoutTail.Value, stderrTail.Value);
                    var message = AddDiagnosticPath(
                        $"The debug application did not connect within {LaunchConnectionTimeout.TotalSeconds:0} seconds. The process is still running and was not terminated." + details);
                    diagnostics?.WriteLine(message);
                    ReportLaunchFailure(message, "Application connection timed out");
                }

                await exit.ConfigureAwait(false);
                await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
                if (token.IsCancellationRequested || epoch != _epoch) return;
                diagnostics?.WriteLine($"Process exited with code {process.ExitCode}.");
                if (process.ExitCode == 0 && connected.IsCompleted) return;
                if (process.ExitCode != 0 || !connected.IsCompleted)
                {
                    var message = $"Debug application exited {(connected.IsCompleted ? "" : "before connecting ")}(exit code {process.ExitCode})." +
                        (string.IsNullOrWhiteSpace(stdoutTail.Value) && string.IsNullOrWhiteSpace(stderrTail.Value)
                            ? " Check the launch diagnostics and game log."
                            : FormatOutputTails(stdoutTail.Value, stderrTail.Value));
                    message = AddDiagnosticPath(message);
                    diagnostics?.WriteLine(message);
                    ReportLaunchFailure(message, "Application exited");
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error) when (error is IOException or InvalidOperationException or ObjectDisposedException)
            {
                if (token.IsCancellationRequested || epoch != _epoch) return;
                Error = AddDiagnosticPath(error.Message);
                Status = "Could not monitor application";
                diagnostics?.WriteLine($"Launch monitor failed: {error}");
                Serilog.Log.Error(error, "Could not monitor debug application");
                Changed?.Invoke();
            }
        }
    }
    private static string FormatOutputTails(string stdout, string stderr)
    {
        var output = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(stdout)) output.AppendLine().Append("stdout:").AppendLine().Append(stdout.Trim());
        if (!string.IsNullOrWhiteSpace(stderr)) output.AppendLine().Append("stderr:").AppendLine().Append(stderr.Trim());
        return output.ToString();
    }

    private static async Task CaptureOutputAsync(StreamReader reader, string stream, int processId, string secretToken,
        BoundedOutputTail tail, DebugLaunchDiagnostics? diagnostics)
    {
        var buffer = new char[1024];
        var line = new StringBuilder();
        try
        {
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) > 0)
            {
                var chunk = new string(buffer, 0, count);
                tail.Add(chunk);
                diagnostics?.WriteOutput(stream, chunk);
                foreach (var character in chunk)
                {
                    if (character == '\n')
                    {
                        WriteAppOutput(stream, processId, line.ToString(), secretToken);
                        line.Clear();
                    }
                    else if (character != '\r')
                    {
                        line.Append(character);
                        if (line.Length > 4096) DrainSafeOutputPrefix(stream, processId, line, secretToken);
                    }
                }
            }
        }
        finally
        {
            if (line.Length > 0) WriteAppOutput(stream, processId, line.ToString(), secretToken);
            diagnostics?.CompleteOutput(stream);
        }
    }

    private static void DrainSafeOutputPrefix(string stream, int processId, StringBuilder pending, string secretToken)
    {
        var buffered = pending.ToString();
        var commitLength = Math.Max(0, buffered.Length - secretToken.Length + 1);
        var searchFrom = 0;
        while (commitLength > 0 && searchFrom < commitLength)
        {
            var tokenIndex = buffered.IndexOf(secretToken, searchFrom, StringComparison.OrdinalIgnoreCase);
            if (tokenIndex < 0 || tokenIndex >= commitLength) break;
            if (tokenIndex + secretToken.Length > commitLength)
            {
                commitLength = tokenIndex;
                break;
            }
            searchFrom = tokenIndex + 1;
        }
        if (commitLength <= 0) return;
        WriteAppOutput(stream, processId, buffered[..commitLength], secretToken);
        pending.Remove(0, commitLength);
    }

    private static void WriteAppOutput(string stream, int processId, string text, string secretToken)
    {
        if (string.IsNullOrEmpty(text)) return;
        var safeText = text.Replace(secretToken, "[REDACTED]", StringComparison.OrdinalIgnoreCase);
        var logger = Serilog.Log.ForContext("LogChannel", "Game").ForContext("DebugAppProcessId", processId);
        if (stream == "stderr") logger.Error("[Debug App stderr] {Output}", safeText);
        else logger.Information("[Debug App stdout] {Output}", safeText);
    }

    private sealed class BoundedOutputTail(string secretToken)
    {
        private const int MaximumTailCharacters = 8192;
        private readonly object _gate = new();
        private readonly StringBuilder _value = new();
        public string Value
        {
            get
            {
                lock (_gate)
                {
                    var redacted = _value.ToString().Replace(secretToken, "[REDACTED]", StringComparison.OrdinalIgnoreCase);
                    return redacted.Length > MaximumTailCharacters ? redacted[^MaximumTailCharacters..] : redacted;
                }
            }
        }
        public void Add(string value)
        {
            lock (_gate)
            {
                _value.Append(value);
                var retainedCharacters = MaximumTailCharacters + secretToken.Length;
                if (_value.Length > retainedCharacters) _value.Remove(0, _value.Length - retainedCharacters);
            }
        }
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
