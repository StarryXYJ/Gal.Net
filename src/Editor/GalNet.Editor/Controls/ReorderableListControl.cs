using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using GalNet.Core.Variable;
using GalNet.Editor.ViewModels;
using System.Windows.Input;
using AvaloniaControl = Avalonia.Controls.Control;
using Avalonia.VisualTree;

namespace GalNet.Editor.Controls;

/// <summary>
/// Base class for controls that support drag-to-reorder.
/// Subclass must provide a ListBox and call InitializeDragDrop() after InitializeComponent().
/// </summary>
public class ReorderableListControl : UserControl
{
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty = AvaloniaProperty.Register<ReorderableListControl, IEnumerable?>(nameof(ItemsSource));
    public static readonly StyledProperty<ICommand?> MoveCommandProperty = AvaloniaProperty.Register<ReorderableListControl, ICommand?>(nameof(MoveCommand));
    public static readonly StyledProperty<ICommand?> AddCommandProperty = AvaloniaProperty.Register<ReorderableListControl, ICommand?>(nameof(AddCommand));
    public static readonly StyledProperty<ICommand?> RemoveCommandProperty = AvaloniaProperty.Register<ReorderableListControl, ICommand?>(nameof(RemoveCommand));
    public static readonly StyledProperty<IBrush?> HighlightBrushProperty = AvaloniaProperty.Register<ReorderableListControl, IBrush?>(nameof(HighlightBrush));
    public static readonly StyledProperty<IEnumerable<ConditionVariableSuggestion>?> ConditionSuggestionsProperty = AvaloniaProperty.Register<ReorderableListControl, IEnumerable<ConditionVariableSuggestion>?>(nameof(ConditionSuggestions));
    public static readonly StyledProperty<IEnumerable<ProjectVariableDefinition>?> ValidationVariablesProperty = AvaloniaProperty.Register<ReorderableListControl, IEnumerable<ProjectVariableDefinition>?>(nameof(ValidationVariables));
    public IEnumerable? ItemsSource { get => GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public ICommand? MoveCommand { get => GetValue(MoveCommandProperty); set => SetValue(MoveCommandProperty, value); }
    public ICommand? AddCommand { get => GetValue(AddCommandProperty); set => SetValue(AddCommandProperty, value); }
    public ICommand? RemoveCommand { get => GetValue(RemoveCommandProperty); set => SetValue(RemoveCommandProperty, value); }
    public IBrush? HighlightBrush { get => GetValue(HighlightBrushProperty); set => SetValue(HighlightBrushProperty, value); }
    public IEnumerable<ConditionVariableSuggestion>? ConditionSuggestions { get => GetValue(ConditionSuggestionsProperty); set => SetValue(ConditionSuggestionsProperty, value); }
    public IEnumerable<ProjectVariableDefinition>? ValidationVariables { get => GetValue(ValidationVariablesProperty); set => SetValue(ValidationVariablesProperty, value); }

    private ListBox? _listBox;
    private ScrollViewer? _scrollViewer;
    private readonly Canvas _overlay = new() { IsHitTestVisible = false, ZIndex = 9999 };
    private readonly ContentControl _preview = new() { IsHitTestVisible = false, Opacity = .85, IsVisible = false };
    private readonly Border _line = new() { Height = 3, Background = Brush.Parse("#8F72FF"), IsVisible = false };
    private int _dragIndex = -1, _targetIndex = -1;
    private object? _dragItem;
    private Point _start;
    private bool _dragging;

    protected ReorderableListControl()
    {
        // Use handledEventsToo to ensure PointerMoved/Released fire even when ListBox consumes events
        AddHandler(PointerMovedEvent, OnDragPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnDragPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerWheelChangedEvent, OnDragPointerWheelChanged, RoutingStrategies.Tunnel, handledEventsToo: true);

        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        Loaded -= OnLoaded;
        AttachOverlay();
    }

    /// <summary>
    /// Subclass must call this after InitializeComponent() to register the ListBox.
    /// </summary>
    protected void InitializeDragDrop(ListBox listBox)
    {
        _listBox = listBox;
        AttachOverlay();
        Dispatcher.UIThread.Post(() => _scrollViewer = listBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(), DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Subclass binds this to the drag handle button's PointerPressed event.
    /// </summary>
    protected void OnDragHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (_listBox is null || sender is not AvaloniaControl control || control.DataContext is not { } item) return;
        _dragIndex = _listBox.Items.IndexOf(item);
        if (_dragIndex < 0) return;
        _dragItem = item;
        _targetIndex = -1; _dragging = false; _start = e.GetPosition(this);
        _preview.Content = item; _preview.ContentTemplate = _listBox.ItemTemplate;
        e.Pointer.Capture(this); e.Handled = true;
    }

    /// <summary>
    /// Subclass binds this to the control's PointerMoved event.
    /// </summary>
    protected void OnDragPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragIndex < 0 || _listBox is null) return;
        var pos = e.GetPosition(this);
        if (!_dragging)
        {
            var delta = pos - _start;
            if (Math.Abs(delta.X) < 5 && Math.Abs(delta.Y) < 5) return;
            _dragging = true; _preview.IsVisible = true; SetOpacity(_dragIndex, .3);
        }
        Canvas.SetLeft(_preview, pos.X + 12); Canvas.SetTop(_preview, pos.Y - _preview.Bounds.Height / 2);
        AutoScroll(pos);
        UpdateTarget(pos);
    }

    private void OnDragPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!_dragging || _scrollViewer is null) return;
        ScrollBy(-e.Delta.Y * 48);
        UpdateTarget(e.GetPosition(this));
        e.Handled = true;
    }

    private void AutoScroll(Point pointer)
    {
        if (_scrollViewer is null || _listBox?.TranslatePoint(default, this) is not { } origin) return;
        const double edge = 42;
        var top = origin.Y;
        var bottom = origin.Y + _listBox.Bounds.Height;
        if (pointer.Y < top + edge) ScrollBy(-14);
        else if (pointer.Y > bottom - edge) ScrollBy(14);
    }

    private void ScrollBy(double delta)
    {
        if (_scrollViewer is null) return;
        var maximum = Math.Max(0, _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height);
        var y = Math.Clamp(_scrollViewer.Offset.Y + delta, 0, maximum);
        _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, y);
    }

    /// <summary>
    /// Subclass binds this to the control's PointerReleased or PointerCaptureLost event.
    /// </summary>
    protected void OnDragPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragIndex < 0) return;
        try
        {
            if (_dragging && _targetIndex >= 0 && _targetIndex != _dragIndex && _dragItem is not null)
            {
                var request = new ReorderRequest(_dragItem, _targetIndex);
                if (MoveCommand?.CanExecute(request) == true) MoveCommand.Execute(request);
            }
        }
        finally
        {
            CleanupDrag(e);
        }
    }

    private void CleanupDrag(PointerReleasedEventArgs e)
    {
        ResetAllItemOpacities();
        // Collection moves can recycle containers after the command returns.
        Dispatcher.UIThread.Post(ResetAllItemOpacities, DispatcherPriority.Background);
        _preview.IsVisible = false;
        _line.IsVisible = false;
        _dragItem = null;
        _dragIndex = _targetIndex = -1;
        _dragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void AttachOverlay()
    {
        if (Content is not Panel panel || _overlay.Parent is not null) return;
        _overlay.Children.Add(_line); _overlay.Children.Add(_preview); panel.Children.Add(_overlay);
    }
    private void SetOpacity(int index, double opacity) { if (_listBox?.ContainerFromIndex(index) is AvaloniaControl c) c.Opacity = opacity; }
    private void ResetAllItemOpacities()
    {
        if (_listBox is null) return;
        for (var index = 0; index < _listBox.ItemCount; index++)
            SetOpacity(index, 1.0);
    }
    private void UpdateTarget(Point p)
    {
        if (_listBox is null) return;
        var target = _listBox.ItemCount;
        for (var i = 0; i < _listBox.ItemCount; i++) if (_listBox.ContainerFromIndex(i) is AvaloniaControl c && c.TranslatePoint(default, this) is { } at && p.Y >= at.Y && p.Y <= at.Y + c.Bounds.Height) { target = p.Y - at.Y < c.Bounds.Height / 2 ? i : i + 1; break; }
        if (target > _dragIndex) target--; _targetIndex = target;
        if (target < 0 || target >= _listBox.ItemCount || _listBox.ContainerFromIndex(target) is not AvaloniaControl item || item.TranslatePoint(default, this) is not { } pos) { _line.IsVisible = false; return; }
        _line.Background = HighlightBrush ?? Brush.Parse("#8F72FF");
        _line.Width = item.Bounds.Width; Canvas.SetLeft(_line, pos.X); Canvas.SetTop(_line, pos.Y - 2); _line.IsVisible = true;
    }

}
