using System;
using Avalonia.Controls;
using Avalonia.Input;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Controls;

public partial class GraphNodeControl : UserControl
{
    public event EventHandler<GraphNodePointerPressedEventArgs>? NodePointerPressed;
    public event EventHandler<PointerEventArgs>? NodePointerMoved;
    public event EventHandler<PointerReleasedEventArgs>? NodePointerReleased;
    public event EventHandler<GraphNodeEventArgs>? NodeDoubleTapped;
    public event EventHandler<GraphNodeEventArgs>? NodeRightTapped;
    public event EventHandler<GraphConnectorPointerPressedEventArgs>? ConnectorPointerPressed;
    public event EventHandler<GraphConnectorPointerReleasedEventArgs>? ConnectorPointerReleased;

    public GraphNodeControl()
    {
        InitializeComponent();
        NodeChrome.PointerPressed += OnNodeChromePointerPressed;
        PointerMoved += (_, e) => NodePointerMoved?.Invoke(this, e);
        PointerReleased += (_, e) =>
        {
            NodePointerReleased?.Invoke(this, e);
        };
        DoubleTapped += (_, e) =>
        {
            if (DataContext is GraphNode node)
            {
                NodeDoubleTapped?.Invoke(this, new GraphNodeEventArgs(node, e));
                e.Handled = true;
            }
        };
        RightTapped += (_, e) =>
        {
            if (DataContext is GraphNode node)
            {
                NodeRightTapped?.Invoke(this, new GraphNodeEventArgs(node, e));
                e.Handled = true;
            }
        };
    }

    private void OnNodeChromePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Focus();

        if (DataContext is GraphNode node)
        {
            NodePointerPressed?.Invoke(this, new GraphNodePointerPressedEventArgs(node, e));
        }
    }

    private void OnConnectorPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Avalonia.Controls.Control { DataContext: GraphConnector connector })
        {
            ConnectorPointerPressed?.Invoke(this, new GraphConnectorPointerPressedEventArgs(connector, e));
            e.Handled = true;
        }
    }

    private void OnConnectorPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Avalonia.Controls.Control { DataContext: GraphConnector connector })
        {
            ConnectorPointerReleased?.Invoke(this, new GraphConnectorPointerReleasedEventArgs(connector, e));
            e.Handled = true;
        }
    }
}

public sealed class GraphNodeEventArgs : EventArgs
{
    public GraphNode Node { get; }
    public TappedEventArgs OriginalEventArgs { get; }

    public GraphNodeEventArgs(GraphNode node, TappedEventArgs originalEventArgs)
    {
        Node = node;
        OriginalEventArgs = originalEventArgs;
    }
}

public sealed class GraphNodePointerPressedEventArgs : EventArgs
{
    public GraphNode Node { get; }
    public PointerPressedEventArgs OriginalEventArgs { get; }

    public GraphNodePointerPressedEventArgs(GraphNode node, PointerPressedEventArgs originalEventArgs)
    {
        Node = node;
        OriginalEventArgs = originalEventArgs;
    }
}

public sealed class GraphConnectorPointerPressedEventArgs : EventArgs
{
    public GraphConnector Connector { get; }
    public PointerPressedEventArgs OriginalEventArgs { get; }

    public GraphConnectorPointerPressedEventArgs(GraphConnector connector, PointerPressedEventArgs originalEventArgs)
    {
        Connector = connector;
        OriginalEventArgs = originalEventArgs;
    }
}

public sealed class GraphConnectorPointerReleasedEventArgs : EventArgs
{
    public GraphConnector Connector { get; }
    public PointerReleasedEventArgs OriginalEventArgs { get; }

    public GraphConnectorPointerReleasedEventArgs(GraphConnector connector, PointerReleasedEventArgs originalEventArgs)
    {
        Connector = connector;
        OriginalEventArgs = originalEventArgs;
    }
}
