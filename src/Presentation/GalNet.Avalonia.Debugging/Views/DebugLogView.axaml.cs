using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace GalNet.Avalonia.Debugging.Views;

public partial class DebugLogView : UserControl
{
    private readonly GameDebugLogStore _logStore;
    private ScrollViewer? _scrollViewer;
    private bool _followTail = true;

    private bool _attached;
    private int _generation;
    public ObservableCollection<GameDebugLogLine> Lines { get; } = [];
    public GameDebugViewModel Actions { get; }
    public event Action? CloseRequested;

    public DebugLogView(GameDebugLogStore logStore, GameDebugViewModel actions)
    {
        _logStore = logStore;
        Actions = actions;
        InitializeComponent();
        DataContext = this;
        AttachedToVisualTree += OnLogListAttached;
        DetachedFromVisualTree += OnDetached;
    }

    private void OnLogListAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _attached = true;
        var generation = ++_generation;
        Lines.Clear();
        foreach (var line in _logStore.GetSnapshot()) Lines.Add(line);
        _logStore.LineAdded += OnLineAdded;
        _logStore.Cleared += OnLogsCleared;
        Dispatcher.UIThread.Post(() => { if (generation == _generation) AttachScrollViewer(); }, DispatcherPriority.Background);
    }

    private void AttachScrollViewer()
    {
        if (!_attached) return;
        if (_scrollViewer is not null) _scrollViewer.ScrollChanged -= OnScrollChanged;
        _scrollViewer = LogListBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (_scrollViewer is not null)
        {
            _scrollViewer.ScrollChanged += OnScrollChanged;
            _followTail = true;
            Dispatcher.UIThread.Post(ScrollToEnd, DispatcherPriority.Background);
        }
    }

    private void OnLineAdded(GameDebugLogLine line)
    {
        var generation = _generation;
        Dispatcher.UIThread.Post(() =>
        {
            if (!_attached || generation != _generation) return;
            Lines.Add(line);
            while (Lines.Count > GameDebugLogStore.Capacity) Lines.RemoveAt(0);
            if (_followTail)
                Dispatcher.UIThread.Post(ScrollToEnd, DispatcherPriority.Background);
        });
    }

    private void OnLogsCleared()
    {
        var generation = _generation;
        Dispatcher.UIThread.Post(() => { if (_attached && generation == _generation) Lines.Clear(); });
    }

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

    private void OnCloseLogsClick(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        // Appending a line changes Extent but not the user's Offset. Do not mistake that
        // layout update for a request to stop following the tail.
        if (Math.Abs(e.OffsetDelta.Y) > 0.01)
            _followTail = IsAtBottom();
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        ++_generation;
        _logStore.LineAdded -= OnLineAdded;
        _logStore.Cleared -= OnLogsCleared;
        if (_scrollViewer is not null)
            _scrollViewer.ScrollChanged -= OnScrollChanged;
        _scrollViewer = null;
    }
}
