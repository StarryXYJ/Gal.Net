using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Sample.Avalonia.Debug;

namespace GalNet.Sample.Avalonia.Views;

public partial class DebugLogWindow : Ursa.Controls.UrsaWindow
{
    private readonly SampleDebugLogStore _logStore;
    private ScrollViewer? _scrollViewer;

    public ObservableCollection<SampleDebugLogLine> Lines { get; } = [];

    public DebugLogWindow(SampleDebugLogStore logStore)
    {
        _logStore = logStore;
        InitializeComponent();
        DataContext = this;
        foreach (var line in _logStore.GetSnapshot())
            Lines.Add(line);

        _logStore.LineAdded += OnLineAdded;
        _logStore.Cleared += OnLogsCleared;
        LogListBox.AttachedToVisualTree += OnLogListAttached;
        Closed += OnClosed;
    }

    private void OnLogListAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _scrollViewer = LogListBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
    }

    private void OnLineAdded(SampleDebugLogLine line) =>
        Dispatcher.UIThread.Post(() =>
        {
            var followTail = IsAtBottom();
            Lines.Add(line);
            if (followTail)
                Dispatcher.UIThread.Post(ScrollToEnd, DispatcherPriority.Background);
        });

    private void OnLogsCleared() => Dispatcher.UIThread.Post(Lines.Clear);

    private bool IsAtBottom()
    {
        if (_scrollViewer is null)
            return true;

        return _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height - _scrollViewer.Offset.Y <= 1;
    }

    private void ScrollToEnd()
    {
        if (LogListBox.ItemCount > 0)
            LogListBox.ScrollIntoView(LogListBox.ItemCount - 1);
    }

    private void OnClearLogsClick(object? sender, RoutedEventArgs e) => _logStore.Clear();

    private void OnClosed(object? sender, EventArgs e)
    {
        _logStore.LineAdded -= OnLineAdded;
        _logStore.Cleared -= OnLogsCleared;
    }
}
