using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Core.Graph;
using GalNet.Editor.Controls;
using GalNet.Core.Entry;

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
    private static readonly IBrush SelectedBackground = Brush.Parse("#312A55");
    private static readonly IBrush DefaultBackground = Brush.Parse("#1E1E2E");
    private static readonly IBrush SelectedBorderBrush = Brush.Parse("#8F72FF");
    private static readonly IBrush EntryBorderBrush = Brush.Parse("#A891FF");
    private static readonly IBrush DefaultBorderBrush = Brush.Parse("#45475A");
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
    public IBrush NodeBackground => IsSelected ? SelectedBackground : DefaultBackground;
    public IBrush NodeBorderBrush => IsSelected ? SelectedBorderBrush : IsEntryNode ? EntryBorderBrush : DefaultBorderBrush;
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
                Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["speaker"] = "Alice", ["content"] = "Hello GalNet"
                }
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
    private static readonly IBrush DefaultStrokeBrush = Brush.Parse("#8F72FF");
    private static readonly IBrush SelectedStrokeBrush = Brush.Parse("#A891FF");
    public GraphNode From { get; }
    public GraphNode To { get; }
    public string Id { get; }

    [ObservableProperty]
    private int _outlet;

    [ObservableProperty]
    private bool _isSelected;

    public IBrush StrokeBrush => IsSelected ? SelectedStrokeBrush : DefaultStrokeBrush;
    public double StrokeThickness => IsSelected ? 4 : 2.5;

    public double StartX => From.GetConnectorCenter(GraphConnectorKind.Output, Outlet).X;
    public double StartY => From.GetConnectorCenter(GraphConnectorKind.Output, Outlet).Y;
    public double EndX => To.GetConnectorCenter(GraphConnectorKind.Input, 0).X;
    public double EndY => To.GetConnectorCenter(GraphConnectorKind.Input, 0).Y;
    public double ControlOffset => Math.Max(80, Math.Abs(EndX - StartX) * 0.5);
    public string PathData => $"M {StartX},{StartY} C {StartX + ControlOffset},{StartY} {EndX - ControlOffset},{EndY} {EndX},{EndY}";

    public GraphEdge(GraphNode from, GraphNode to, int outlet = 0, string? id = null)
    {
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id;
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
    public string StableId { get; init; } = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private int _id;

    [ObservableProperty]
    private string _type = "";

    [ObservableProperty]
    private string _condition = "";

    [ObservableProperty]
    private Dictionary<string, string> _parameters = new(StringComparer.Ordinal);

    [ObservableProperty]
    private bool _isExpanded = true;

    public ObservableCollection<EntryParameterEditorItemViewModel> ParameterFields { get; } = [];
    /// <summary>Builds the editor projection from the Core registry, the single schema source.</summary>
    public void ConfigureParameterFields(IReadOnlyList<string> speakers, IReadOnlyList<string> variableNames, bool resetValues = false)
    {
        var schema = EntryRegistry.Get(Type);
        var values = resetValues
            ? new Dictionary<string, string>(schema.Defaults, StringComparer.Ordinal)
            : Parameters.Where(pair => schema.Parameters.ContainsKey(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        foreach (var pair in schema.Defaults)
            values.TryAdd(pair.Key, pair.Value);
        if (!Parameters.OrderBy(x => x.Key).SequenceEqual(values.OrderBy(x => x.Key)))
            Parameters = values;
        ParameterFields.Clear();

        foreach (var (id, type) in schema.Parameters)
        {
            var defaultValue = schema.Defaults.GetValueOrDefault(id, "");
            var options = schema.Options.GetValueOrDefault(id, [])
                .Select(value => new EntrySelectOption(value, $"Entry.Option.{(value.Length == 0 ? "None" : value)}"))
                .ToArray();
            var definition = new EntryParameterDefinition(id, type, $"Entry.Parameter.{id}", defaultValue, options);
            var value = Parameters.GetValueOrDefault(id, defaultValue);
            ParameterFields.Add(CreateField(definition, value, speakers, variableNames));
        }
    }

    private EntryParameterEditorItemViewModel CreateField(EntryParameterDefinition definition, string value, IReadOnlyList<string> speakers, IReadOnlyList<string> variableNames) => definition.Type switch
    {
        EntryParameterType.Autocomplete => new AutocompleteEntryParameterEditorItemViewModel(definition, value, speakers, SetParameter),
        EntryParameterType.VariableName => new VariableNameEntryParameterEditorItemViewModel(definition, value, variableNames, SetParameter),
        EntryParameterType.Expression => new ExpressionEntryParameterEditorItemViewModel(definition, value, variableNames, SetParameter),
        EntryParameterType.Integer => new IntegerEntryParameterEditorItemViewModel(definition, value, speakers, SetParameter),
        EntryParameterType.Float => new FloatEntryParameterEditorItemViewModel(definition, value, speakers, SetParameter),
        EntryParameterType.ImageAsset or EntryParameterType.AudioAsset or EntryParameterType.VideoAsset => new AssetEntryParameterEditorItemViewModel(definition, value, speakers, SetParameter),
        EntryParameterType.Select => new SelectEntryParameterEditorItemViewModel(definition, value, speakers, SetParameter),
        _ => new TextEntryParameterEditorItemViewModel(definition, value, speakers, SetParameter)
    };

    private void SetParameter(string id, string value)
    {
        var updated = new Dictionary<string, string>(Parameters, StringComparer.Ordinal) { [id] = value };
        Parameters = updated;
    }
}

public partial class BranchOptionEditorItemViewModel : ObservableObject
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string _text = "";

    [ObservableProperty]
    private string _condition = "";
}

public partial class BranchConditionEditorItemViewModel : ObservableObject
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string _expression = "";
}
