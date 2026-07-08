using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaControl = Avalonia.Controls.Control;

namespace GalNet.Editor.Controls;

/// <summary>
/// Lightweight drag-drop helper for reordering items in a ListBox.
/// Use when a control has multiple draggable lists (e.g., NodeInspectorPanelView).
/// </summary>
public sealed class DragDropHelper
{
    private readonly ListBox _listBox;
    private readonly AvaloniaControl _owner;
    private readonly Action<int, int> _onMove;
    private readonly Border _previewBorder;
    private readonly ContentControl _previewContent;
    private readonly Border _highlightLine;
    private readonly Canvas _overlay;
    private int _dragIndex = -1;
    private int _targetIndex = -1;
    private bool _isDragging;
    private Point _dragStart;

    public DragDropHelper(ListBox listBox, AvaloniaControl owner, Action<int, int> onMove)
    {
        _listBox = listBox;
        _owner = owner;
        _onMove = onMove;

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

        _owner.Loaded += (_, _) =>
        {
            if (_owner is ContentControl cc && cc.Content is Panel panel)
            {
                panel.Children.Add(_overlay);
            }
        };
    }

    public void OnDragHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not AvaloniaControl control)
            return;

        var item = control.DataContext;
        if (item is null)
            return;

        _dragIndex = _listBox.Items.IndexOf(item);
        if (_dragIndex < 0)
            return;

        _isDragging = false;
        _dragStart = e.GetPosition(_owner);
        _targetIndex = -1;

        _previewContent.Content = item;
        _previewContent.ContentTemplate = _listBox.ItemTemplate;

        e.Pointer.Capture(_owner);
        e.Handled = true;
    }

    public void OnPointerMoved(PointerEventArgs e)
    {
        if (_dragIndex < 0)
            return;

        var pos = e.GetPosition(_owner);

        if (!_isDragging)
        {
            var delta = pos - _dragStart;
            if (Math.Abs(delta.X) < 5 && Math.Abs(delta.Y) < 5)
                return;

            _isDragging = true;
            _previewBorder.IsVisible = true;
            SetItemOpacity(_dragIndex, 0.3);
        }

        Canvas.SetLeft(_previewBorder, pos.X + 12);
        Canvas.SetTop(_previewBorder, pos.Y + 12);

        UpdateTarget(pos);
    }

    public void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_dragIndex >= 0)
            SetItemOpacity(_dragIndex, 1.0);

        _previewBorder.IsVisible = false;
        _highlightLine.IsVisible = false;

        if (_isDragging && _dragIndex >= 0 && _targetIndex >= 0 && _targetIndex != _dragIndex)
            _onMove(_dragIndex, _targetIndex);

        _dragIndex = -1;
        _targetIndex = -1;
        _isDragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void UpdateTarget(Point position)
    {
        var itemCount = _listBox.ItemCount;
        var newTarget = -1;

        for (var i = 0; i < itemCount; i++)
        {
            var container = _listBox.ContainerFromIndex(i);
            if (container is not AvaloniaControl itemControl)
                continue;

            var containerPos = itemControl.TranslatePoint(new Point(0, 0), _owner);
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
            ShowHighlight(newTarget);
        }
    }

    private void ShowHighlight(int targetIndex)
    {
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

        var containerPos = itemControl.TranslatePoint(new Point(0, 0), _owner);
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
        var container = _listBox.ContainerFromIndex(index);
        if (container is AvaloniaControl itemControl)
            itemControl.Opacity = opacity;
    }

    public void Dispose()
    {
        if (_overlay.Parent is Panel panel)
            panel.Children.Remove(_overlay);
    }
}