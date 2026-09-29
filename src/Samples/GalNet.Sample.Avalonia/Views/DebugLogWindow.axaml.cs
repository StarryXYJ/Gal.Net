using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Sample.Avalonia.Debug;
using Ursa.Controls;

namespace GalNet.Sample.Avalonia.Views;

public partial class DebugLogWindow : Ursa.Controls.UrsaWindow
{
    private readonly SampleDebugLogStore _logStore;
    private readonly SampleDebugLogDirectory _logDirectory;
    private ScrollViewer? _scrollViewer;
    private bool _followTail = true;

    public ObservableCollection<SampleDebugLogLine> Lines { get; } = [];

    public DebugLogWindow(SampleDebugLogStore logStore, SampleDebugLogDirectory logDirectory)
    {
        _logStore = logStore;
        _logDirectory = logDirectory;
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
        if (_scrollViewer is not null)
        {
            _scrollViewer.ScrollChanged += OnScrollChanged;
            _followTail = true;
            Dispatcher.UIThread.Post(ScrollToEnd, DispatcherPriority.Background);
        }
    }

    private void OnLineAdded(SampleDebugLogLine line) =>
        Dispatcher.UIThread.Post(() =>
        {
            Lines.Add(line);
            if (_followTail)
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
        if (_followTail && LogListBox.ItemCount > 0)
            LogListBox.ScrollIntoView(LogListBox.ItemCount - 1);
    }

    private void OnClearLogsClick(object? sender, RoutedEventArgs e) => _logStore.Clear();

    private async void OnOpenLogDirectoryClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            _logDirectory.Open();
        }
        catch (Exception exception)
        {
            await MessageBox.ShowAsync(this, exception.Message, "打开日志目录失败", button: MessageBoxButton.OK);
        }
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        // Appending a line changes Extent but not the user's Offset. Do not mistake that
        // layout update for a request to stop following the tail.
        if (Math.Abs(e.OffsetDelta.Y) > 0.01)
            _followTail = IsAtBottom();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _logStore.LineAdded -= OnLineAdded;
        _logStore.Cleared -= OnLogsCleared;
        if (_scrollViewer is not null)
            _scrollViewer.ScrollChanged -= OnScrollChanged;
    }
}
