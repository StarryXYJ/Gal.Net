using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using AvaloniaControl = Avalonia.Controls.Control;

namespace GalNet.Editor.Controls;

/// <summary>
/// Base class for controls that support drag-to-reorder.
/// Subclass must provide a ListBox and call InitializeDragDrop() after InitializeComponent().
/// </summary>
public abstract class ReorderableListControl : UserControl
{
    private readonly Border _previewBorder;
    private readonly ContentControl _previewContent;
    private readonly Border _highlightLine;
    private readonly Canvas _overlay;
    private ListBox? _listBox;
    private Panel? _rootPanel;
    private int _dragIndex = -1;
    private int _targetIndex = -1;
    private bool _isDragging;
    private Point _dragStartPoint;

    protected ReorderableListControl()
    {
        _previewContent = new ContentControl();
        _previewBorder = new Border
        {
            Opacity = 0.85,
            Background = Brush.Parse("#2D2D3F"),
            BorderBrush = Brush.Parse("#8F72FF"),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 6),
            Child = _previewContent,
            IsHitTestVisible = false,
            IsVisible = false
        };

        _highlightLine = new Border
        {
            Height = 3,
            Background = Brush.Parse("#8F72FF"),
            CornerRadius = new CornerRadius(1.5),
            IsVisible = false,
            IsHitTestVisible = false
        };

        _overlay = new Canvas
        {
            IsHitTestVisible = false,
            ZIndex = 9999
        };
        _overlay.Children.Add(_highlightLine);
        _overlay.Children.Add(_previewBorder);

        // Use handledEventsToo to ensure PointerMoved/Released fire even when ListBox consumes events
        AddHandler(PointerMovedEvent, OnDragPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnDragPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);

        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        Loaded -= OnLoaded;
        if (Content is Panel panel)
        {
            _rootPanel = panel;
            panel.Children.Add(_overlay);
        }
    }

    /// <summary>
    /// Subclass must call this after InitializeComponent() to register the ListBox.
    /// </summary>
    protected void InitializeDragDrop(ListBox listBox)
    {
        _listBox = listBox;
    }

    /// <summary>
    /// Subclass binds this to the drag handle button's PointerPressed event.
    /// </summary>
    protected void OnDragHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (_listBox is null || sender is not AvaloniaControl control)
            return;

        var item = control.DataContext;
        if (item is null)
            return;

        _dragIndex = _listBox.Items.IndexOf(item);
        if (_dragIndex < 0)
            return;

        _isDragging = false;
        _dragStartPoint = e.GetPosition(this);
        _targetIndex = -1;

        _previewContent.Content = item;
        _previewContent.ContentTemplate = _listBox.ItemTemplate;

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    /// <summary>
    /// Subclass binds this to the control's PointerMoved event.
    /// </summary>
    protected void OnDragPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragIndex < 0 || _listBox is null)
            return;

        var pos = e.GetPosition(this);

        if (!_isDragging)
        {
            var delta = pos - _dragStartPoint;
            if (Math.Abs(delta.X) < 5 && Math.Abs(delta.Y) < 5)
                return;

            _isDragging = true;
            _previewBorder.IsVisible = true;
            SetItemOpacity(_dragIndex, 0.3);
        }

        Canvas.SetLeft(_previewBorder, pos.X + 12);
        Canvas.SetTop(_previewBorder, pos.Y + 12);

        UpdateTargetIndex(pos);
    }

    /// <summary>
    /// Subclass binds this to the control's PointerReleased or PointerCaptureLost event.
    /// </summary>
    protected void OnDragPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        CleanupDrag();
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    /// <summary>
    /// Subclass overrides this to perform the actual move.
    /// </summary>
    protected abstract void OnMoveItem(int fromIndex, int toIndex);

    private void UpdateTargetIndex(Point position)
    {
        if (_listBox is null)
            return;

        var itemCount = _listBox.ItemCount;
        var newTarget = -1;

        for (var i = 0; i < itemCount; i++)
        {
            var container = _listBox.ContainerFromIndex(i);
            if (container is not AvaloniaControl itemControl)
                continue;

            var containerPos = itemControl.TranslatePoint(new Point(0, 0), this);
            if (containerPos is null)
                continue;

            var relativeY = position.Y - containerPos.Value.Y;
            var halfHeight = itemControl.Bounds.Height / 2;

            if (relativeY >= 0 && relativeY <= itemControl.Bounds.Height)
            {
                newTarget = relativeY < halfHeight ? i : i + 1;
                break;
            }
        }

        if (newTarget < 0)
            newTarget = itemCount;

        if (newTarget > _dragIndex)
            newTarget--;

        if (newTarget != _targetIndex)
        {
            _targetIndex = newTarget;
            ShowHighlightAt(newTarget);
        }
    }

    private void ShowHighlightAt(int targetIndex)
    {
        if (_listBox is null)
            return;

        if (targetIndex < 0 || targetIndex >= _listBox.ItemCount)
        {
            _highlightLine.IsVisible = false;
            return;
        }

        var container = _listBox.ContainerFromIndex(targetIndex);
        if (container is not AvaloniaControl itemControl)
        {
            _highlightLine.IsVisible = false;
            return;
        }

        var containerPos = itemControl.TranslatePoint(new Point(0, 0), this);
        if (containerPos is null)
        {
            _highlightLine.IsVisible = false;
            return;
        }

        _highlightLine.Width = itemControl.Bounds.Width;
        Canvas.SetLeft(_highlightLine, containerPos.Value.X);
        Canvas.SetTop(_highlightLine, containerPos.Value.Y - 2);
        _highlightLine.IsVisible = true;
    }

    private void SetItemOpacity(int index, double opacity)
    {
        if (_listBox is null) return;
        var container = _listBox.ContainerFromIndex(index);
        if (container is AvaloniaControl itemControl)
            itemControl.Opacity = opacity;
    }

    private void CleanupDrag()
    {
        if (_dragIndex >= 0)
            SetItemOpacity(_dragIndex, 1.0);

        _previewBorder.IsVisible = false;
        _highlightLine.IsVisible = false;

        if (_isDragging && _dragIndex >= 0 && _targetIndex >= 0 && _targetIndex != _dragIndex)
            OnMoveItem(_dragIndex, _targetIndex);

        _dragIndex = -1;
        _targetIndex = -1;
        _isDragging = false;
    }
}