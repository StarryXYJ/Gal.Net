using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using Serilog.Core;
using Serilog.Events;

namespace GalNet.Editor.Services;

public sealed class EditorLogSink : ILogEventSink
{
    public static EditorLogSink Instance { get; } = new();

    public ObservableCollection<LogLine> Lines { get; } = [];

    public event Action<LogLine>? LineAdded;

    private EditorLogSink()
    {
    }

    public void Emit(LogEvent logEvent)
    {
        var line = new LogLine
        {
            Timestamp = logEvent.Timestamp,
            Level = logEvent.Level,
            Message = logEvent.RenderMessage(),
            Exception = logEvent.Exception?.ToString(),
            Source = GetSource(logEvent)
        };

        Dispatcher.UIThread.Post(() =>
        {
            Lines.Add(line);
            while (Lines.Count > 500)
                Lines.RemoveAt(0);
            LineAdded?.Invoke(line);
        });
    }

    private static string GetSource(LogEvent logEvent)
    {
        if (logEvent.Properties.TryGetValue("SourceContext", out var ctx))
            return ctx.ToString().Trim('"');

        return string.Empty;
    }
}

public sealed class LogLine
{
    public DateTimeOffset Timestamp { get; init; }
    public LogEventLevel Level { get; init; }
    public string Message { get; init; } = "";
    public string? Exception { get; init; }
    public string Source { get; init; } = "";

    public bool IsError => Level >= LogEventLevel.Error;
    public bool IsWarning => Level == LogEventLevel.Warning;

    public string DisplayText => $"[{Timestamp:HH:mm:ss} {Level.ToString()[..3].ToUpperInvariant()}] {Message}";

    public string FullText => Exception is null ? DisplayText : DisplayText + Environment.NewLine + Exception;
}
