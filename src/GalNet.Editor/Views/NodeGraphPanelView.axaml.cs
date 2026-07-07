using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using GalNet.Editor.ViewModels;
using Serilog;

namespace GalNet.Editor.Views;

public partial class NodeGraphPanelView : UserControl
{
    private readonly ScaleTransform _scaleTransform = new(1, 1);
    private readonly TranslateTransform _translateTransform = new();
    private GraphNodeViewModel? _draggedNode;
    private GraphConnectorViewModel? _pendingConnector;
    private Point _dragStart;
    private Point _lastCanvasPointerPosition;
    private double _nodeStartX;
    private double _nodeStartY;
    private bool _isPanning;
    private Point _panStart;
    private double _panStartX;
    private double _panStartY;
    private double _zoom = 1;
    private bool _isPointerInsideGraph;

    public NodeGraphPanelView()
    {
        InitializeComponent();

        GraphCanvas.RenderTransform = new TransformGroup
        {
            Children =
            {
                _scaleTransform,
                _translateTransform
            }
        };
    }

    private NodeGraphPanelViewModel? ViewModel => DataContext as NodeGraphPanelViewModel;

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space)
            return;

        if (!IsKeyboardFocusWithin || !_isPointerInsideGraph)
            return;

        ShowCreateNodeMenu(ClampToGraph(_lastCanvasPointerPosition), null);
        e.Handled = true;
    }

    private void OnViewportPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        _zoom = Math.Clamp(_zoom + e.Delta.Y * 0.08, 0.35, 2.5);
        _scaleTransform.ScaleX = _zoom;
        _scaleTransform.ScaleY = _zoom;
        e.Handled = true;
    }

    private void OnViewportPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Focus();
        _isPointerInsideGraph = true;
        _lastCanvasPointerPosition = e.GetPosition(GraphCanvas);

        if (e.GetCurrentPoint(Viewport).Properties.IsLeftButtonPressed && _pendingConnector is null)
        {
            _isPanning = true;
            _panStart = e.GetPosition(Viewport);
            _panStartX = _translateTransform.X;
            _panStartY = _translateTransform.Y;
            e.Pointer.Capture(Viewport);
            e.Handled = true;
        }
    }

    private void OnViewportPointerMoved(object? sender, PointerEventArgs e)
    {
        _isPointerInsideGraph = true;
        _lastCanvasPointerPosition = e.GetPosition(GraphCanvas);

        if (!_isPanning)
            return;

        var current = e.GetPosition(Viewport);
        _translateTransform.X = _panStartX + current.X - _panStart.X;
        _translateTransform.Y = _panStartY + current.Y - _panStart.Y;
        e.Handled = true;
    }

    private void OnViewportPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _lastCanvasPointerPosition = e.GetPosition(GraphCanvas);

        if (_pendingConnector is { Kind: GraphConnectorKind.Output } output)
        {
            ShowCreateNodeMenu(ClampToGraph(_lastCanvasPointerPosition), output);
            _pendingConnector = null;
            e.Handled = true;
        }
        else
        {
            _pendingConnector = null;
        }

        _isPanning = false;
        e.Pointer.Capture(null);
    }

    private void OnViewportRightTapped(object? sender, TappedEventArgs e)
    {
        _lastCanvasPointerPosition = e.GetPosition(GraphCanvas);
        ShowCreateNodeMenu(ClampToGraph(_lastCanvasPointerPosition), null);
        e.Handled = true;
    }

    private void OnViewportPointerEntered(object? sender, PointerEventArgs e)
    {
        _isPointerInsideGraph = true;
        _lastCanvasPointerPosition = e.GetPosition(GraphCanvas);
    }

    private void OnViewportPointerExited(object? sender, PointerEventArgs e)
    {
        _isPointerInsideGraph = false;
    }

    private void OnNodePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Avalonia.Controls.Control { DataContext: GraphNodeViewModel node })
            return;

        if (!e.GetCurrentPoint(sender as Avalonia.Controls.Control).Properties.IsLeftButtonPressed)
            return;

        ViewModel?.Workspace.SelectNode(node);
        _draggedNode = node;
        _dragStart = e.GetPosition(GraphCanvas);
        _nodeStartX = node.X;
        _nodeStartY = node.Y;
        e.Pointer.Capture(sender as Avalonia.Controls.Control);
        e.Handled = true;
    }

    private void OnNodePointerMoved(object? sender, PointerEventArgs e)
    {
        _lastCanvasPointerPosition = e.GetPosition(GraphCanvas);

        if (_draggedNode is null)
            return;

        var current = e.GetPosition(GraphCanvas);
        _draggedNode.X = _nodeStartX + current.X - _dragStart.X;
        _draggedNode.Y = _nodeStartY + current.Y - _dragStart.Y;
        e.Handled = true;
    }

    private void OnNodePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _draggedNode = null;
        e.Pointer.Capture(null);
    }

    private void OnNodeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Avalonia.Controls.Control { DataContext: GraphNodeViewModel node })
            ViewModel?.Workspace.OpenGroupEditor(node);

        e.Handled = true;
    }

    private void OnNodeRightTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Avalonia.Controls.Control { DataContext: GraphNodeViewModel node } control)
            return;

        ViewModel?.Workspace.SelectNode(node);

        var menu = new MenuFlyout();
        var deleteItem = new MenuItem { Header = "删除" };
        deleteItem.Click += (_, _) => ViewModel?.Workspace.DeleteNode(node);
        menu.Items.Add(deleteItem);
        menu.ShowAt(control);
        e.Handled = true;
    }

    private void OnConnectorPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Avalonia.Controls.Control { DataContext: GraphConnectorViewModel connector })
            return;

        _pendingConnector = connector;
        ViewModel?.Workspace.SelectNode(connector.Node);
        e.Handled = true;
    }

    private void OnConnectorPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_pendingConnector is null
            || sender is not Avalonia.Controls.Control { DataContext: GraphConnectorViewModel target })
            return;

        ViewModel?.Workspace.Connect(_pendingConnector, target);
        _pendingConnector = null;
        e.Handled = true;
    }

    private void ShowCreateNodeMenu(Point position, GraphConnectorViewModel? connectFrom)
    {
        var menu = new MenuFlyout();
        AddCreateNodeMenuItem(menu, "线性组", GraphNodeKind.LinearGroup, position, connectFrom);
        AddCreateNodeMenuItem(menu, "选项分支", GraphNodeKind.ChoiceBranch, position, connectFrom);
        AddCreateNodeMenuItem(menu, "条件分支", GraphNodeKind.ConditionBranch, position, connectFrom);
        menu.Placement = PlacementMode.Pointer;
        menu.ShowAt(Viewport, true);
    }

    private void AddCreateNodeMenuItem(
        MenuFlyout menu,
        string header,
        GraphNodeKind kind,
        Point position,
        GraphConnectorViewModel? connectFrom)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) =>
        {
            var node = ViewModel?.Workspace.AddNode(kind, position.X, position.Y);
            if (node is not null && connectFrom is not null)
                ViewModel?.Workspace.Connect(connectFrom, node.InputConnectors[0]);

            Log.Information("Node menu created {NodeKind} at {X}, {Y}", kind, position.X, position.Y);
        };
        menu.Items.Add(item);
    }

    private Point ClampToGraph(Point position)
    {
        var maxX = Math.Max(0, GraphCanvas.Width - 220);
        var maxY = Math.Max(0, GraphCanvas.Height - 140);
        return new Point(
            Math.Clamp(position.X, 0, maxX),
            Math.Clamp(position.Y, 0, maxY));
    }
}
