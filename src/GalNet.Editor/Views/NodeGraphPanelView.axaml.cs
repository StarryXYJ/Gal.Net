using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using GalNet.Editor.Controls;
using GalNet.Editor.ViewModels;
using Serilog;

namespace GalNet.Editor.Views;

public partial class NodeGraphPanelView : UserControl
{
    private GraphNode? _draggedNode;
    private readonly Dictionary<GraphNode, Point> _dragStartPositions = [];
    private GraphConnector? _pendingConnector;
    private GraphConnector? _previewTargetConnector;
    private Point _dragStartWorld;
    private Point _lastViewportPointerPosition;
    private bool _isSelecting;
    private bool _selectionMoved;
    private bool _nodeDragMoved;
    private Point _selectionStartWorld;
    private bool _isPointerInsideGraph;
    private bool _hasAppliedViewportState;
    private const double ConnectorSnapDistance = 32;

    public NodeGraphPanelView()
    {
        InitializeComponent();

        AttachedToVisualTree += (_, _) => ApplyViewportState();
        Viewport.ViewChanged += (_, _) => SaveViewportState();
        Viewport.SizeChanged += (_, _) => ApplyViewportState();
    }

    private EditorWorkspaceViewModel? Workspace => DataContext as EditorWorkspaceViewModel;

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space)
        {
            if (e.Key == Key.Delete)
            {
                Workspace?.DeleteSelection();
                e.Handled = true;
            }
            return;
        }

        if (!IsKeyboardFocusWithin || !_isPointerInsideGraph)
            return;

        ShowCreateNodeMenu(ViewportToWorld(_lastViewportPointerPosition), null);
        e.Handled = true;
    }

    private void OnViewportPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Focus();
        _isPointerInsideGraph = true;
        _lastViewportPointerPosition = e.GetPosition(Viewport);
        UpdatePendingConnection(_lastViewportPointerPosition);

        if (!e.GetCurrentPoint(Viewport).Properties.IsLeftButtonPressed
            || _pendingConnector is not null
            || (!ReferenceEquals(e.Source, GraphCanvas) && !ReferenceEquals(e.Source, Viewport)))
            return;

        _isSelecting = true;
        _selectionMoved = false;
        _selectionStartWorld = ViewportToWorld(_lastViewportPointerPosition);
        Workspace?.ClearSelection();
        UpdateSelectionRectangle(_selectionStartWorld, _selectionStartWorld);
        e.Pointer.Capture(Viewport);
        e.Handled = true;
    }

    private void OnViewportPointerMoved(object? sender, PointerEventArgs e)
    {
        _isPointerInsideGraph = true;
        _lastViewportPointerPosition = e.GetPosition(Viewport);
        UpdatePendingConnection(_lastViewportPointerPosition);

        if (_isSelecting)
        {
            var currentWorld = ViewportToWorld(_lastViewportPointerPosition);
            _selectionMoved = _selectionMoved
                || Math.Abs(currentWorld.X - _selectionStartWorld.X) > 4
                || Math.Abs(currentWorld.Y - _selectionStartWorld.Y) > 4;
            UpdateSelectionRectangle(_selectionStartWorld, currentWorld);
            e.Handled = true;
        }
    }

    private void OnViewportPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _lastViewportPointerPosition = e.GetPosition(Viewport);

        if (_isSelecting)
        {
            CompleteSelection(ViewportToWorld(_lastViewportPointerPosition));
            _isSelecting = false;
            SelectionRectangle.IsVisible = false;
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        if (_pendingConnector is not null)
        {
            if (_previewTargetConnector is not null)
            {
                Workspace?.Connect(_pendingConnector, _previewTargetConnector);
            }
            else if (_pendingConnector.Kind == GraphConnectorKind.Output)
            {
                ShowCreateNodeMenu(ViewportToWorld(_lastViewportPointerPosition), _pendingConnector);
            }

            ClearPendingConnection();
            e.Pointer.Capture(null);
            e.Handled = true;
        }
        else
        {
            ClearPendingConnection();
        }
    }

    private void UpdateSelectionRectangle(Point start, Point end)
    {
        var left = Math.Min(start.X, end.X);
        var top = Math.Min(start.Y, end.Y);
        var width = Math.Abs(end.X - start.X);
        var height = Math.Abs(end.Y - start.Y);

        Canvas.SetLeft(SelectionRectangle, left);
        Canvas.SetTop(SelectionRectangle, top);
        SelectionRectangle.Width = width;
        SelectionRectangle.Height = height;
        SelectionRectangle.IsVisible = true;
    }

    private void CompleteSelection(Point end)
    {
        if (!_selectionMoved)
        {
            Workspace?.ClearSelection();
            return;
        }

        var rect = new Rect(_selectionStartWorld, end).Normalize();
        var selected = Workspace?.Nodes
            .Where(node => rect.Intersects(new Rect(
                node.X,
                node.Y,
                GraphLayoutMetrics.NodeWidth,
                GraphLayoutMetrics.GetNodeHeight(node.InputConnectors.Count, node.OutputConnectors.Count))))
            .ToList() ?? [];

        if (selected.Count > 0)
            Workspace?.SelectNodes(selected);
        else
            Workspace?.ClearSelection();
    }

    private void OnViewportRightTapped(object? sender, TappedEventArgs e)
    {
        _lastViewportPointerPosition = e.GetPosition(Viewport);
        if (Workspace?.SelectedNodes.Count > 1)
        {
            ShowSelectionMenu();
            e.Handled = true;
            return;
        }

        ShowCreateNodeMenu(ViewportToWorld(_lastViewportPointerPosition), null);
        e.Handled = true;
    }

    private void OnViewportPointerEntered(object? sender, PointerEventArgs e)
    {
        _isPointerInsideGraph = true;
        _lastViewportPointerPosition = e.GetPosition(Viewport);
    }

    private void OnViewportPointerExited(object? sender, PointerEventArgs e)
    {
        _isPointerInsideGraph = false;
    }

    private void OnNodePointerPressed(object? sender, GraphNodePointerPressedEventArgs e)
    {
        if (!e.OriginalEventArgs.GetCurrentPoint(sender as Avalonia.Controls.Control).Properties.IsLeftButtonPressed)
            return;

        if (Workspace is null)
            return;

        var wasSelected = e.Node.IsSelected;
        if (!wasSelected)
            Workspace.SelectNode(e.Node);

        _draggedNode = e.Node;
        _nodeDragMoved = false;
        _dragStartWorld = ViewportToWorld(e.OriginalEventArgs.GetPosition(Viewport));
        _dragStartPositions.Clear();
        var dragNodes = wasSelected ? Workspace.SelectedNodes.ToList() : [e.Node];
        foreach (var node in dragNodes)
            _dragStartPositions[node] = new Point(node.X, node.Y);

        e.OriginalEventArgs.Pointer.Capture(sender as Avalonia.Controls.Control);
        e.OriginalEventArgs.Handled = true;
    }

    private void OnNodePointerMoved(object? sender, PointerEventArgs e)
    {
        _lastViewportPointerPosition = e.GetPosition(Viewport);

        if (_draggedNode is null)
            return;

        if (!e.GetCurrentPoint(Viewport).Properties.IsLeftButtonPressed)
        {
            EndNodeDrag(e.Pointer);
            e.Handled = true;
            return;
        }

        var current = ViewportToWorld(_lastViewportPointerPosition);
        var deltaX = current.X - _dragStartWorld.X;
        var deltaY = current.Y - _dragStartWorld.Y;
        if (!_nodeDragMoved && Math.Abs(deltaX) < 2 && Math.Abs(deltaY) < 2)
            return;

        _nodeDragMoved = true;
        foreach (var (node, start) in _dragStartPositions)
        {
            node.X = Math.Clamp(start.X + deltaX, 0, GraphCanvas.Width - GraphLayoutMetrics.NodeWidth);
            node.Y = Math.Clamp(
                start.Y + deltaY,
                0,
                GraphCanvas.Height - GraphLayoutMetrics.GetNodeHeight(node.InputConnectors.Count, node.OutputConnectors.Count));
        }
        e.Handled = true;
    }

    private void OnNodePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left)
            return;

        EndNodeDrag(e.Pointer);
        e.Handled = true;
    }

    private void EndNodeDrag(IPointer pointer)
    {
        if (_draggedNode is not null && _nodeDragMoved)
            Workspace?.SaveGraphDocument();

        _draggedNode = null;
        _nodeDragMoved = false;
        _dragStartPositions.Clear();
        pointer.Capture(null);
    }

    private void OnNodeDoubleTapped(object? sender, GraphNodeEventArgs e)
    {
        Workspace?.OpenGroupEditor(e.Node);
        e.OriginalEventArgs.Handled = true;
    }

    private void OnNodeRightTapped(object? sender, GraphNodeEventArgs e)
    {
        if (sender is not Avalonia.Controls.Control control)
            return;

        if (Workspace is null)
            return;

        if (e.Node.IsSelected && Workspace.SelectedNodes.Count > 1)
        {
            ShowSelectionMenu(control);
            e.OriginalEventArgs.Handled = true;
            return;
        }

        Workspace.SelectNode(e.Node);

        var menu = new MenuFlyout();
        var deleteItem = new MenuItem
        {
            Header = Workspace.Localize(e.Node.CanDelete ? "Graph.Menu.Delete" : "Graph.Menu.CannotDeleteEntry"),
            IsEnabled = e.Node.CanDelete
        };
        deleteItem.Click += (_, _) => Workspace?.DeleteNode(e.Node);
        menu.Items.Add(deleteItem);
        menu.Placement = PlacementMode.Pointer;
        menu.ShowAt(control, true);
        e.OriginalEventArgs.Handled = true;
    }

    private void OnEdgePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Avalonia.Controls.Control { DataContext: GraphEdge edge })
            return;

        if (!e.GetCurrentPoint(sender as Avalonia.Controls.Control).Properties.IsLeftButtonPressed)
            return;

        Focus();
        Workspace?.SelectEdge(edge);
        e.Handled = true;
    }

    private void OnEdgeRightTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Avalonia.Controls.Control { DataContext: GraphEdge edge } control)
            return;

        Focus();
        Workspace?.SelectEdge(edge);

        var menu = new MenuFlyout();
        var deleteItem = new MenuItem { Header = Workspace?.Localize("Graph.Menu.Delete") ?? "Delete" };
        deleteItem.Click += (_, _) => Workspace?.DeleteEdge(edge);
        menu.Items.Add(deleteItem);
        menu.Placement = PlacementMode.Pointer;
        menu.ShowAt(control, true);
        e.Handled = true;
    }

    private void ShowSelectionMenu(Avalonia.Controls.Control? target = null)
    {
        var menu = new MenuFlyout();
        var deleteItem = new MenuItem { Header = Workspace?.Localize("Graph.Menu.Delete") ?? "Delete" };
        deleteItem.Click += (_, _) => Workspace?.DeleteSelection();
        menu.Items.Add(deleteItem);
        menu.Placement = PlacementMode.Pointer;
        menu.ShowAt(target ?? Viewport, true);
    }

    private void OnConnectorPointerPressed(object? sender, GraphConnectorPointerPressedEventArgs e)
    {
        if (!e.OriginalEventArgs.GetCurrentPoint(sender as Avalonia.Controls.Control).Properties.IsLeftButtonPressed)
            return;

        _pendingConnector = e.Connector;
        Workspace?.SelectNode(e.Connector.Node);
        _lastViewportPointerPosition = e.OriginalEventArgs.GetPosition(Viewport);
        e.OriginalEventArgs.Pointer.Capture(Viewport);
        UpdatePendingConnection(_lastViewportPointerPosition);
        e.OriginalEventArgs.Handled = true;
    }

    private void OnConnectorPointerReleased(object? sender, GraphConnectorPointerReleasedEventArgs e)
    {
        if (_pendingConnector is null)
            return;

        Workspace?.Connect(_pendingConnector, e.Connector);
        ClearPendingConnection();
        e.OriginalEventArgs.Pointer.Capture(null);
        e.OriginalEventArgs.Handled = true;
    }

    private void UpdatePendingConnection(Point viewportPosition)
    {
        if (_pendingConnector is null)
            return;

        var start = _pendingConnector.Node.GetConnectorCenter(_pendingConnector.Kind, _pendingConnector.Index);
        var target = FindNearestConnector(viewportPosition);
        var end = target?.Node.GetConnectorCenter(target.Kind, target.Index) ?? ViewportToWorld(viewportPosition);

        SetPreviewTarget(target);
        PendingConnectionPath.Data = Geometry.Parse(CreateConnectionPath(start, end));
        PendingConnectionPath.IsVisible = true;
    }

    private GraphConnector? FindNearestConnector(Point viewportPosition)
    {
        if (_pendingConnector is null || Workspace is null)
            return null;

        GraphConnector? nearest = null;
        var nearestDistance = ConnectorSnapDistance;

        foreach (var connector in Workspace.Nodes.SelectMany(n => n.InputConnectors.Concat(n.OutputConnectors)))
        {
            if (!CanConnectPreview(_pendingConnector, connector))
                continue;

            var center = Viewport.WorldToViewport(connector.Node.GetConnectorCenter(connector.Kind, connector.Index));
            var distance = Math.Sqrt(Math.Pow(center.X - viewportPosition.X, 2) + Math.Pow(center.Y - viewportPosition.Y, 2));
            if (distance >= nearestDistance)
                continue;

            nearest = connector;
            nearestDistance = distance;
        }

        return nearest;
    }

    private static bool CanConnectPreview(GraphConnector first, GraphConnector second)
    {
        return !ReferenceEquals(first.Node, second.Node) && first.Kind != second.Kind;
    }

    private void SetPreviewTarget(GraphConnector? connector)
    {
        if (ReferenceEquals(_previewTargetConnector, connector))
            return;

        if (_previewTargetConnector is not null)
            _previewTargetConnector.IsPreviewTarget = false;

        _previewTargetConnector = connector;

        if (_previewTargetConnector is not null)
            _previewTargetConnector.IsPreviewTarget = true;
    }

    private void ClearPendingConnection()
    {
        _pendingConnector = null;
        SetPreviewTarget(null);
        PendingConnectionPath.IsVisible = false;
        PendingConnectionPath.Data = null;
    }

    private static string CreateConnectionPath(Point start, Point end)
    {
        var controlOffset = Math.Max(80, Math.Abs(end.X - start.X) * 0.5);
        return $"M {start.X},{start.Y} C {start.X + controlOffset},{start.Y} {end.X - controlOffset},{end.Y} {end.X},{end.Y}";
    }

    private void ShowCreateNodeMenu(Point position, GraphConnector? connectFrom)
    {
        var menu = new MenuFlyout();
        AddCreateNodeMenuItem(menu, Workspace?.Localize("Graph.Menu.CreateLinearGroup") ?? "Linear Group", GraphNodeKind.LinearGroup, position, connectFrom);
        AddCreateNodeMenuItem(menu, Workspace?.Localize("Graph.Menu.CreateChoiceBranch") ?? "Choice Branch", GraphNodeKind.ChoiceBranch, position, connectFrom);
        AddCreateNodeMenuItem(menu, Workspace?.Localize("Graph.Menu.CreateConditionBranch") ?? "Condition Branch", GraphNodeKind.ConditionBranch, position, connectFrom);
        menu.Placement = PlacementMode.Pointer;
        menu.ShowAt(Viewport, true);
    }

    private void AddCreateNodeMenuItem(
        MenuFlyout menu,
        string header,
        GraphNodeKind kind,
        Point position,
        GraphConnector? connectFrom)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) =>
        {
            var x = Math.Clamp(position.X, 0, GraphCanvas.Width - GraphLayoutMetrics.NodeWidth);
            var y = Math.Clamp(position.Y, 0, GraphCanvas.Height - GraphLayoutMetrics.NodeMinHeight);
            var node = Workspace?.AddNode(kind, x, y);
            if (node is not null && connectFrom is not null)
                Workspace?.Connect(connectFrom, node.InputConnectors[0]);

            Log.Information("Node menu created {NodeKind} at {X}, {Y}", kind, x, y);
        };
        menu.Items.Add(item);
    }

    private Point ViewportToWorld(Point viewportPoint) => Viewport.ViewportToWorld(viewportPoint);

    private void ApplyViewportState()
    {
        if (_hasAppliedViewportState || Viewport.Bounds.Width <= 0 || Viewport.Bounds.Height <= 0)
            return;

        var state = Workspace?.GraphViewport;
        if (state is { OffsetX: not 0 } or { OffsetY: not 0 })
        {
            Viewport.SetViewport(state.Zoom, state.OffsetX, state.OffsetY);
        }
        else
        {
            Viewport.CenterOnWorldBounds(state?.Zoom ?? 1.15);
        }

        if (!AnyNodeVisible())
            CenterOnPrimaryNode();

        _hasAppliedViewportState = true;
    }

    private void SaveViewportState()
    {
        if (Workspace?.GraphViewport is not { } state)
            return;

        state.Zoom = Viewport.Zoom;
        state.OffsetX = Viewport.OffsetX;
        state.OffsetY = Viewport.OffsetY;
        Workspace?.SaveGraphViewport();
    }

    private bool AnyNodeVisible()
    {
        if (Workspace?.Nodes.Count is not > 0)
            return true;

        return Workspace?.Nodes.Any(node =>
        {
            var topLeft = Viewport.WorldToViewport(new Point(node.X, node.Y));
            var bottomRight = Viewport.WorldToViewport(new Point(
                node.X + GraphLayoutMetrics.NodeWidth,
                node.Y + GraphLayoutMetrics.GetNodeHeight(node.InputConnectors.Count, node.OutputConnectors.Count)));
            return bottomRight.X >= 0
                && bottomRight.Y >= 0
                && topLeft.X <= Viewport.Bounds.Width
                && topLeft.Y <= Viewport.Bounds.Height;
        }) ?? true;
    }

    private void CenterOnPrimaryNode()
    {
        var node = Workspace?.Nodes.FirstOrDefault(n => n.IsRoot)
            ?? Workspace?.Nodes.FirstOrDefault();
        if (node is null)
            return;

        Viewport.CenterOn(new Point(node.X + 94, node.Y + 48));
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        ApplyViewportState();
    }
}
