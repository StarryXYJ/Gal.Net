using System;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Core.Graph;
using GalNet.Editor.Controls;

namespace GalNet.Editor.Models.Graph;
public enum GraphNodeKind
{
    Entry,
    LinearGroup,
    ChoiceBranch,
    ConditionBranch
}

public enum GraphConnectorKind
{
    Input,
    Output
}

public partial class GraphNode : ObservableObject
{
    public Node Node { get; }
    public GraphNodeKind NodeKind { get; }

    [ObservableProperty]
    private double _x;

    [ObservableProperty]
    private double _y;

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(NodeBackground));
        OnPropertyChanged(nameof(NodeBorderBrush));
        OnPropertyChanged(nameof(NodeBorderThickness));
    }

    [ObservableProperty]
    private bool _isRoot;

    public string Id => Node.Id;
    public string KindLabel => NodeKind switch
    {
        GraphNodeKind.Entry => "Entry",
        GraphNodeKind.LinearGroup => "Linear Group",
        GraphNodeKind.ChoiceBranch => "Choice Branch",
        GraphNodeKind.ConditionBranch => "Condition Branch",
        _ => NodeKind.ToString()
    };

    public bool IsEntryNode => NodeKind == GraphNodeKind.Entry;
    public bool CanDelete => !IsEntryNode;
    public IBrush NodeBackground => IsSelected ? Brush.Parse("#312A55") : Brush.Parse("#1E1E2E");
    public IBrush NodeBorderBrush => IsSelected ? Brush.Parse("#8F72FF") : IsEntryNode ? Brush.Parse("#A891FF") : Brush.Parse("#45475A");
    public double NodeBorderThickness => IsSelected ? 2 : 1;

    public ObservableCollection<GraphConnector> InputConnectors { get; } = [];
    public ObservableCollection<GraphConnector> OutputConnectors { get; } = [];
    public ObservableCollection<EntryEditorItemViewModel> Entries { get; } = [];
    public ObservableCollection<BranchOptionEditorItemViewModel> Options { get; } = [];
    public ObservableCollection<BranchConditionEditorItemViewModel> Conditions { get; } = [];

    public int EntryCount => Entries.Count;

    public GraphNode(Node node, GraphNodeKind nodeKind)
    {
        Node = node;
        NodeKind = nodeKind;
        Name = string.IsNullOrWhiteSpace(node.Name) ? KindLabel : node.Name;

        if (NodeKind == GraphNodeKind.LinearGroup)
        {
            Entries.Add(new EntryEditorItemViewModel
            {
                Id = 1,
                Type = "text",
                Parameters = "speaker=Alice; text=Hello GalNet"
            });
        }

        if (NodeKind == GraphNodeKind.ConditionBranch)
            Conditions.Add(new BranchConditionEditorItemViewModel { Expression = "true" });

        if (NodeKind == GraphNodeKind.ChoiceBranch && Options.Count == 0)
            Options.Add(new BranchOptionEditorItemViewModel { Text = "Option 1" });

        RefreshConnectors();
    }

    public void RefreshConnectors()
    {
        InputConnectors.Clear();
        OutputConnectors.Clear();

        if (!IsEntryNode)
            InputConnectors.Add(new GraphConnector(this, GraphConnectorKind.Input, 0));

        var outputCount = NodeKind switch
        {
            GraphNodeKind.Entry => 1,
            GraphNodeKind.ChoiceBranch => Math.Max(1, Options.Count),
            GraphNodeKind.ConditionBranch => Math.Max(1, Conditions.Count),
            _ => 1
        };

        for (var i = 0; i < outputCount; i++)
            OutputConnectors.Add(new GraphConnector(this, GraphConnectorKind.Output, i));

        OnPropertyChanged(nameof(EntryCount));
    }

    public Point GetConnectorCenter(GraphConnectorKind kind, int index)
    {
        var connectors = kind == GraphConnectorKind.Input ? InputConnectors : OutputConnectors;
        var inputCount = InputConnectors.Count;
        var outputCount = OutputConnectors.Count;
        var count = Math.Max(1, connectors.Count);
        var nodeHeight = GraphLayoutMetrics.GetNodeHeight(inputCount, outputCount);
        var rowHeight = GraphLayoutMetrics.ConnectorHitSize + GraphLayoutMetrics.ConnectorVerticalMargin * 2;
        var firstY = nodeHeight / 2 - count * rowHeight / 2 + rowHeight / 2;
        var x = kind == GraphConnectorKind.Input
            ? GraphLayoutMetrics.NodeHorizontalPadding + GraphLayoutMetrics.ConnectorHitSize / 2
            : GraphLayoutMetrics.NodeWidth - GraphLayoutMetrics.NodeHorizontalPadding - GraphLayoutMetrics.ConnectorHitSize / 2;

        return new Point(X + x, Y + firstY + index * rowHeight);
    }

    partial void OnNameChanged(string value)
    {
        Node.Name = value;
    }
}

public partial class GraphConnector : ObservableObject
{
    public GraphNode Node { get; }
    public GraphConnectorKind Kind { get; }
    public int Index { get; }

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isPreviewTarget;

    public GraphConnector(GraphNode node, GraphConnectorKind kind, int index)
    {
        Node = node;
        Kind = kind;
        Index = index;
    }
}

public partial class GraphEdge : ObservableObject
{
    public GraphNode From { get; }
    public GraphNode To { get; }

    [ObservableProperty]
    private int _outlet;

    [ObservableProperty]
    private bool _isSelected;

    public IBrush StrokeBrush => IsSelected ? Brush.Parse("#A891FF") : Brush.Parse("#8F72FF");
    public double StrokeThickness => IsSelected ? 4 : 2.5;

    public double StartX => From.GetConnectorCenter(GraphConnectorKind.Output, Outlet).X;
    public double StartY => From.GetConnectorCenter(GraphConnectorKind.Output, Outlet).Y;
    public double EndX => To.GetConnectorCenter(GraphConnectorKind.Input, 0).X;
    public double EndY => To.GetConnectorCenter(GraphConnectorKind.Input, 0).Y;
    public double ControlOffset => Math.Max(80, Math.Abs(EndX - StartX) * 0.5);
    public string PathData => $"M {StartX},{StartY} C {StartX + ControlOffset},{StartY} {EndX - ControlOffset},{EndY} {EndX},{EndY}";

    public GraphEdge(GraphNode from, GraphNode to, int outlet = 0)
    {
        From = from;
        To = to;
        _outlet = outlet;

        From.PropertyChanged += (_, e) => OnNodeMoved(e.PropertyName);
        To.PropertyChanged += (_, e) => OnNodeMoved(e.PropertyName);
        From.OutputConnectors.CollectionChanged += (_, _) => OnNodeMoved(nameof(GraphNode.Y));
        To.InputConnectors.CollectionChanged += (_, _) => OnNodeMoved(nameof(GraphNode.Y));
    }

    private void OnNodeMoved(string? propertyName)
    {
        if (propertyName is not (nameof(GraphNode.X) or nameof(GraphNode.Y)))
            return;

        OnPropertyChanged(nameof(StartX));
        OnPropertyChanged(nameof(StartY));
        OnPropertyChanged(nameof(EndX));
        OnPropertyChanged(nameof(EndY));
        OnPropertyChanged(nameof(PathData));
    }

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(StrokeBrush));
        OnPropertyChanged(nameof(StrokeThickness));
    }

    partial void OnOutletChanged(int value)
    {
        OnPropertyChanged(nameof(StartX));
        OnPropertyChanged(nameof(StartY));
        OnPropertyChanged(nameof(PathData));
    }
}

public partial class EntryEditorItemViewModel : ObservableObject
{
    [ObservableProperty]
    private int _id;

    [ObservableProperty]
    private string _type = "";

    [ObservableProperty]
    private string _condition = "";

    [ObservableProperty]
    private string _parameters = "";
}

public partial class BranchOptionEditorItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _text = "";

    [ObservableProperty]
    private string _condition = "";
}

public partial class BranchConditionEditorItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _expression = "";
}
