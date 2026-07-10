using System;
using Avalonia.Controls;
using Avalonia.Input;
using AvaloniaControl = Avalonia.Controls.Control;

namespace GalNet.Editor.Controls;

/// <summary>
/// Lightweight drag-drop helper for reordering items in a ListBox.
/// Use when a control has multiple draggable lists (e.g., NodeInspectorPanelView).
/// </summary>
public sealed class DragDropHelper
{
    private readonly ReorderDragController _dragController;

    public DragDropHelper(ListBox listBox, AvaloniaControl owner, Action<int, int> onMove)
    {
        _dragController = new ReorderDragController(listBox, owner, onMove);

        owner.Loaded += (_, _) =>
        {
            if (owner is ContentControl cc && cc.Content is Panel panel)
                _dragController.AttachOverlay(panel);
        };
    }

    public void OnDragHandlePressed(object? sender, PointerPressedEventArgs e) => _dragController.OnDragHandlePressed(sender, e);

    public void OnPointerMoved(PointerEventArgs e) => _dragController.OnPointerMoved(e);

    public void OnPointerReleased(PointerReleasedEventArgs e) => _dragController.OnPointerReleased(e);

    public void Dispose()
        => _dragController.Dispose();
}
