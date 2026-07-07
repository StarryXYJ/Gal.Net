using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using Serilog.Core;
using Serilog.Events;

namespace GalNet.Editor.Services;

public sealed class EditorLogSink : ILogEventSink
{
    public static EditorLogSink Instance { get; } = new();

    public ObservableCollection<string> Lines { get; } = [];

    private EditorLogSink()
    {
    }

    public void Emit(LogEvent logEvent)
    {
        var line = $"[{logEvent.Timestamp:HH:mm:ss} {logEvent.Level.ToString()[..3].ToUpperInvariant()}] {logEvent.RenderMessage()}";
        if (logEvent.Exception is not null)
            line += Environment.NewLine + logEvent.Exception;

        Dispatcher.UIThread.Post(() =>
        {
            Lines.Insert(0, line);
            while (Lines.Count > 500)
                Lines.RemoveAt(Lines.Count - 1);
        });
    }
}
