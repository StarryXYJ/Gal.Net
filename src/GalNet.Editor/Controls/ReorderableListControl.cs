using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaControl = Avalonia.Controls.Control;

namespace GalNet.Editor.Controls;

/// <summary>
/// Base class for controls that support drag-to-reorder.
/// Subclass must provide a ListBox and call InitializeDragDrop() after InitializeComponent().
/// </summary>
public abstract class ReorderableListControl : UserControl
{
    private ReorderDragController? _dragController;
    private ListBox? _listBox;

    protected ReorderableListControl()
    {
        // Use handledEventsToo to ensure PointerMoved/Released fire even when ListBox consumes events
        AddHandler(PointerMovedEvent, OnDragPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnDragPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);

        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        Loaded -= OnLoaded;
        if (Content is Panel panel && _dragController is not null)
            _dragController.AttachOverlay(panel);
    }

    /// <summary>
    /// Subclass must call this after InitializeComponent() to register the ListBox.
    /// </summary>
    protected void InitializeDragDrop(ListBox listBox)
    {
        _listBox = listBox;
        _dragController = new ReorderDragController(listBox, this, OnMoveItem);
        if (Content is Panel panel)
            _dragController.AttachOverlay(panel);
    }

    /// <summary>
    /// Subclass binds this to the drag handle button's PointerPressed event.
    /// </summary>
    protected void OnDragHandlePressed(object? sender, PointerPressedEventArgs e)
        => _dragController?.OnDragHandlePressed(sender, e);

    /// <summary>
    /// Subclass binds this to the control's PointerMoved event.
    /// </summary>
    protected void OnDragPointerMoved(object? sender, PointerEventArgs e)
        => _dragController?.OnPointerMoved(e);

    /// <summary>
    /// Subclass binds this to the control's PointerReleased or PointerCaptureLost event.
    /// </summary>
    protected void OnDragPointerReleased(object? sender, PointerReleasedEventArgs e)
        => _dragController?.OnPointerReleased(e);

    /// <summary>
    /// Subclass overrides this to perform the actual move.
    /// </summary>
    protected abstract void OnMoveItem(int fromIndex, int toIndex);

}
