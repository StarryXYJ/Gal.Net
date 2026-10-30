using System.Globalization;
using Serilog.Core;
using Serilog.Events;

namespace GalNet.Avalonia.Debugging;

/// <summary>Bounded in-memory Serilog sink used by the optional sample debug UI.</summary>
public sealed class GameDebugLogStore : ILogEventSink
{
    public const int Capacity = 500;
    private readonly object _gate = new();
    private readonly List<GameDebugLogLine> _lines = [];
    private bool _enabled;

    public static GameDebugLogStore Shared { get; } = new();

    public bool IsEnabled
    {
        get
        {
            lock (_gate)
                return _enabled;
        }
    }

    public event Action<GameDebugLogLine>? LineAdded;
    public event Action? Cleared;

    public void Enable()
    {
        lock (_gate)
            _enabled = true;
    }

    public IReadOnlyList<GameDebugLogLine> GetSnapshot()
    {
        lock (_gate)
            return _lines.ToArray();
    }

    public void Clear()
    {
        lock (_gate)
            _lines.Clear();

        Cleared?.Invoke();
    }

    public void Emit(LogEvent logEvent)
    {
        GameDebugLogLine? line = null;
        lock (_gate)
        {
            if (!_enabled)
                return;

            line = new GameDebugLogLine(
                logEvent.Timestamp,
                logEvent.Level,
                logEvent.RenderMessage(CultureInfo.InvariantCulture),
                logEvent.Exception?.ToString(),
                GetSource(logEvent));
            _lines.Add(line);
            if (_lines.Count > Capacity)
                _lines.RemoveAt(0);
        }

        LineAdded?.Invoke(line);
    }

    private static string GetSource(LogEvent logEvent) =>
        logEvent.Properties.TryGetValue("SourceContext", out var source)
            ? source.ToString().Trim('\"')
            : string.Empty;
}

public sealed record GameDebugLogLine(
    DateTimeOffset Timestamp,
    LogEventLevel Level,
    string Message,
    string? Exception,
    string Source)
{
    public string DisplayText => $"[{Timestamp:HH:mm:ss} {Level.ToString()[..3].ToUpperInvariant()}] {Message}";
    public string FullText => Exception is null ? DisplayText : DisplayText + Environment.NewLine + Exception;
}
