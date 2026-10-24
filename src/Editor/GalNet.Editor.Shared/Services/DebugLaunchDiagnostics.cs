using System.Text;

namespace GalNet.Editor.Shared.Services;

/// <summary>Bounded, per-launch support log stored beside the Sample App's game logs.</summary>
internal sealed class DebugLaunchDiagnostics : IDisposable
{
    private const int MaximumBytes = 1024 * 1024;
    private const int RetainedFiles = 20;
    private readonly object _gate = new();
    private readonly StreamWriter _writer;
    private readonly Dictionary<string, StringBuilder> _pendingOutput = new(StringComparer.Ordinal);
    private string? _secret;
    private int _writtenBytes;
    private bool _truncated;
    private bool _disposed;

    private DebugLaunchDiagnostics(string path)
    {
        FullPath = path;
        _writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false))
        {
            AutoFlush = true
        };
    }

    public string FullPath { get; }

    public void RegisterSecret(string secret)
    {
        lock (_gate) _secret = secret;
    }

    public static DebugLaunchDiagnostics? TryCreate(string profileRoot)
    {
        try
        {
            var directory = Path.Combine(profileRoot, "logs");
            Directory.CreateDirectory(directory);
            RetainRecentLogs(directory);
            var name = $"debug-launch-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.log";
            var diagnostics = new DebugLaunchDiagnostics(Path.Combine(directory, name));
            Serilog.Log.ForContext("LogChannel", "Game")
                .Information("Debug launch diagnostics log: {DiagnosticLogPath}", diagnostics.FullPath);
            return diagnostics;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Serilog.Log.Warning(error, "Could not create a persistent debug launch log under {ProfileRoot}", profileRoot);
            return null;
        }
    }

    public void WriteLine(string message)
    {
        lock (_gate)
        {
            if (_disposed || _truncated) return;
            AppendLineUnsafe($"[{DateTimeOffset.Now:O}] {message}");
        }
    }

    public void WriteOutput(string stream, string text)
    {
        lock (_gate)
        {
            if (_disposed || _truncated) return;
            if (!_pendingOutput.TryGetValue(stream, out var pending)) _pendingOutput[stream] = pending = new();
            pending.Append(text);
            var commitLength = _secret is { Length: > 0 } secret ? pending.Length - secret.Length + 1 : pending.Length;
            if (commitLength <= 0) return;
            var buffered = pending.ToString();
            if (_secret is { Length: > 0 } token)
            {
                var searchFrom = 0;
                while (searchFrom < commitLength)
                {
                    var tokenIndex = buffered.IndexOf(token, searchFrom, StringComparison.OrdinalIgnoreCase);
                    if (tokenIndex < 0 || tokenIndex >= commitLength) break;
                    if (tokenIndex + token.Length > commitLength)
                    {
                        commitLength = tokenIndex;
                        break;
                    }
                    searchFrom = tokenIndex + 1;
                }
            }
            if (commitLength <= 0) return;
            WriteOutputUnsafe(stream, buffered[..commitLength]);
            pending.Remove(0, commitLength);
        }
    }

    public void CompleteOutput(string stream)
    {
        lock (_gate)
        {
            if (_disposed || _truncated || !_pendingOutput.Remove(stream, out var pending)) return;
            WriteOutputUnsafe(stream, pending.ToString());
        }
    }

    private void WriteOutputUnsafe(string stream, string text)
    {
        var safeText = _secret is { Length: > 0 } token
            ? text.Replace(token, "[REDACTED]", StringComparison.OrdinalIgnoreCase)
            : text;
        foreach (var line in safeText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            if (line.Length > 0)
                AppendLineUnsafe($"[{DateTimeOffset.Now:O}] [{stream}] {line}");
            if (_truncated) return;
        }
    }

    private void AppendLineUnsafe(string line)
    {
        var byteCount = Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;
        if (_writtenBytes + byteCount > MaximumBytes)
        {
            const string notice = "[diagnostics] Output capture stopped after reaching the 1 MiB per-launch limit.";
            var noticeBytes = Encoding.UTF8.GetByteCount(notice) + Environment.NewLine.Length;
            if (_writtenBytes + noticeBytes <= MaximumBytes)
            {
                _writer.WriteLine(notice);
                _writtenBytes += noticeBytes;
            }
            _truncated = true;
            return;
        }

        _writer.WriteLine(line);
        _writtenBytes += byteCount;
    }

    private static void RetainRecentLogs(string directory)
    {
        var logs = Directory.GetFiles(directory, "debug-launch-*.log")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Skip(RetainedFiles - 1);
        foreach (var file in logs)
        {
            try { file.Delete(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _secret = null;
            _pendingOutput.Clear();
            _writer.Dispose();
        }
    }
}
