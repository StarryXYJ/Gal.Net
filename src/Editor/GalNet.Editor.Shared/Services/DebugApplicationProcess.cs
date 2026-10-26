using System.Diagnostics;
using System.Text;
using GalNet.Debugging;

namespace GalNet.Editor.Shared.Services;

/// <summary>Launches and drains an external application's output independently of the debug transport lifetime.</summary>
internal sealed class DebugApplicationProcess(DebugLaunchDiagnostics? diagnostics, Func<bool> isCurrent, Action<string, string> reportFailure)
{
    private static readonly TimeSpan LaunchConnectionTimeout = TimeSpan.FromSeconds(30);
    private readonly DebugLaunchDiagnostics? _diagnostics = diagnostics;
    private readonly Func<bool> _isCurrent = isCurrent;
    private readonly Action<string, string> _reportFailure = reportFailure;
    private string AddDiagnosticPath(string message) => _diagnostics?.FullPath is { } path && !message.Contains(path, StringComparison.OrdinalIgnoreCase)
        ? $"{message}{Environment.NewLine}Diagnostics: {path}" : message;
    public Task Launch(GameDebugLaunchSettings settings, string contentRoot, string profileRoot, DebugInvitation invitation, Task connected)
    {
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
        return MonitorLaunchAsync(process, connected, invitation.Token, _diagnostics);
    }
    private async Task MonitorLaunchAsync(Process process, Task connected, string secretToken, DebugLaunchDiagnostics? diagnostics)
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
                if (first == timeout && _isCurrent() && !connected.IsCompleted && !process.HasExited)
                {
                    var details = FormatOutputTails(stdoutTail.Value, stderrTail.Value);
                    var message = AddDiagnosticPath(
                        $"The debug application did not connect within {LaunchConnectionTimeout.TotalSeconds:0} seconds. The process is still running and was not terminated." + details);
                    diagnostics?.WriteLine(message);
                    _reportFailure(message, "Application connection timed out");
                }

                await exit.ConfigureAwait(false);
                await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
                if (!_isCurrent()) return;
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
                    _reportFailure(message, "Application exited");
                }
            }
            catch (OperationCanceledException) when (!_isCurrent()) { }
            catch (Exception error) when (error is IOException or InvalidOperationException or ObjectDisposedException)
            {
                if (!_isCurrent()) return;
                _reportFailure(AddDiagnosticPath(error.Message), "Could not monitor application");
                diagnostics?.WriteLine($"Launch monitor failed: {error}");
                Serilog.Log.Error(error, "Could not monitor debug application");
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

}
