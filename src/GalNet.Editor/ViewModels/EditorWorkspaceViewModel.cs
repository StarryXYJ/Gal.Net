using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.Settings;
using GalNet.Editor.Dock;
using GalNet.Editor.Project;
using Serilog;

namespace GalNet.Editor.ViewModels;

public partial class EditorWorkspaceViewModel : ObservableObject
{
    private readonly IProjectService _projectService;
    private readonly EditorDockFactory _dockFactory;

    [ObservableProperty]
    private GraphNodeViewModel? _selectedNode;

    [ObservableProperty]
    private GamePreviewPanelViewModel? _activePreview;

    [ObservableProperty]
    private InspectorMode _inspectorMode = InspectorMode.Node;

    [ObservableProperty]
    private string _statusText = "Ready";

    public ObservableCollection<GraphNodeViewModel> Nodes { get; } = [];
    public ObservableCollection<GraphEdgeViewModel> Edges { get; } = [];

    public EditorWorkspaceViewModel(
        IProjectService projectService,
        EditorDockFactory dockFactory)
    {
        _projectService = projectService;
        _dockFactory = dockFactory;
        BuildSampleGraph();
    }

    public void SelectNode(GraphNodeViewModel? node)
    {
        if (SelectedNode is not null)
            SelectedNode.IsSelected = false;

        SelectedNode = node;
        InspectorMode = InspectorMode.Node;

        if (SelectedNode is not null)
            SelectedNode.IsSelected = true;
    }

    public void FocusPreview(GamePreviewPanelViewModel preview)
    {
        ActivePreview = preview;
        InspectorMode = InspectorMode.PreviewVariables;
    }

    public GraphNodeViewModel AddNode(GraphNodeKind kind, double x, double y)
    {
        var index = Nodes.Count(n => n.NodeKind == kind) + 1;
        Node node = kind switch
        {
            GraphNodeKind.LinearGroup => new Group { Name = $"Linear Group {index}" },
            GraphNodeKind.ChoiceBranch => new Branch { Name = $"Choice Branch {index}", BranchType = BranchType.Choice },
            GraphNodeKind.ConditionBranch => new Branch { Name = $"Condition Branch {index}", BranchType = BranchType.Condition },
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

        var vm = new GraphNodeViewModel(node, kind)
        {
            X = x,
            Y = y
        };

        Nodes.Add(vm);
        SelectNode(vm);
        Log.Information("Node created: {NodeName} ({NodeKind})", vm.Name, vm.NodeKind);
        return vm;
    }

    public void DeleteNode(GraphNodeViewModel node)
    {
        var relatedEdges = Edges
            .Where(e => ReferenceEquals(e.From, node) || ReferenceEquals(e.To, node))
            .ToList();

        foreach (var edge in relatedEdges)
            Edges.Remove(edge);

        Nodes.Remove(node);

        if (ReferenceEquals(SelectedNode, node))
            SelectNode(null);

        if (node.NodeKind == GraphNodeKind.LinearGroup)
            _dockFactory.CloseGroupEditor(node.Id);

        Log.Information("Node deleted: {NodeName} ({NodeKind})", node.Name, node.NodeKind);
    }

    public void OpenGroupEditor(GraphNodeViewModel node)
    {
        if (node.NodeKind != GraphNodeKind.LinearGroup)
            return;

        _dockFactory.OpenGroupEditor(node);
        Log.Information("Group editor opened: {NodeName}", node.Name);
    }

    public void Connect(GraphConnectorViewModel first, GraphConnectorViewModel second)
    {
        if (ReferenceEquals(first.Node, second.Node) || first.Kind == second.Kind)
            return;

        var output = first.Kind == GraphConnectorKind.Output ? first : second;
        var input = first.Kind == GraphConnectorKind.Input ? first : second;

        if (Edges.Any(e =>
                ReferenceEquals(e.From, output.Node)
                && e.Outlet == output.Index
                && ReferenceEquals(e.To, input.Node)))
            return;

        Edges.Add(new GraphEdgeViewModel(output.Node, input.Node, output.Index));
        Log.Information("Nodes connected: {From} [{Outlet}] -> {To}", output.Node.Name, output.Index, input.Node.Name);
    }

    [RelayCommand]
    private void AddChoiceOption()
    {
        if (SelectedNode?.NodeKind != GraphNodeKind.ChoiceBranch)
            return;

        SelectedNode.Options.Add(new BranchOptionEditorItemViewModel
        {
            Text = $"Option {SelectedNode.Options.Count + 1}"
        });
        SelectedNode.RefreshConnectors();
        Log.Information("Choice option added: {NodeName}", SelectedNode.Name);
    }

    [RelayCommand]
    private void RemoveChoiceOption(BranchOptionEditorItemViewModel? option)
    {
        if (SelectedNode is null || option is null)
            return;

        SelectedNode.Options.Remove(option);
        SelectedNode.RefreshConnectors();
        RemoveDanglingOutletEdges(SelectedNode);
    }

    [RelayCommand]
    private void AddCondition()
    {
        if (SelectedNode?.NodeKind != GraphNodeKind.ConditionBranch)
            return;

        SelectedNode.Conditions.Add(new BranchConditionEditorItemViewModel
        {
            Expression = "true"
        });
        SelectedNode.RefreshConnectors();
        Log.Information("Condition added: {NodeName}", SelectedNode.Name);
    }

    [RelayCommand]
    private void RemoveCondition(BranchConditionEditorItemViewModel? condition)
    {
        if (SelectedNode is null || condition is null)
            return;

        SelectedNode.Conditions.Remove(condition);
        SelectedNode.RefreshConnectors();
        RemoveDanglingOutletEdges(SelectedNode);
    }

    public async Task CreateProjectAsync(string name, string path)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Project name is required.");

        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Project path is required.");

        var projectPath = Path.Combine(path, name);
        await _projectService.CreateAsync(projectPath, name, new ProjectSettings());
        StatusText = $"Created project {name}";
        Log.Information("Project created from startup page: {Name} at {Path}", name, projectPath);
    }

    private void RemoveDanglingOutletEdges(GraphNodeViewModel node)
    {
        var maxOutlet = node.OutputConnectors.Count - 1;
        var dangling = Edges
            .Where(e => ReferenceEquals(e.From, node) && e.Outlet > maxOutlet)
            .ToList();

        foreach (var edge in dangling)
            Edges.Remove(edge);
    }

    private void BuildSampleGraph()
    {
        var opening = new GraphNodeViewModel(new Group { Name = "Opening" }, GraphNodeKind.LinearGroup)
        {
            X = 120,
            Y = 120,
            IsRoot = true
        };

        var choice = new GraphNodeViewModel(new Branch
        {
            Name = "First Choice",
            BranchType = BranchType.Choice
        }, GraphNodeKind.ChoiceBranch)
        {
            X = 420,
            Y = 160
        };

        var routeA = new GraphNodeViewModel(new Group { Name = "Route A" }, GraphNodeKind.LinearGroup)
        {
            X = 720,
            Y = 80
        };

        var routeB = new GraphNodeViewModel(new Group { Name = "Route B" }, GraphNodeKind.LinearGroup)
        {
            X = 720,
            Y = 260
        };

        choice.Options.Add(new BranchOptionEditorItemViewModel { Text = "Go Route A" });
        choice.Options.Add(new BranchOptionEditorItemViewModel { Text = "Go Route B" });
        choice.RefreshConnectors();

        Nodes.Add(opening);
        Nodes.Add(choice);
        Nodes.Add(routeA);
        Nodes.Add(routeB);

        Edges.Add(new GraphEdgeViewModel(opening, choice));
        Edges.Add(new GraphEdgeViewModel(choice, routeA, 0));
        Edges.Add(new GraphEdgeViewModel(choice, routeB, 1));

        SelectNode(opening);
    }
}

public enum InspectorMode
{
    Node,
    PreviewVariables
}

public enum GraphNodeKind
{
    LinearGroup,
    ChoiceBranch,
    ConditionBranch
}

public enum GraphConnectorKind
{
    Input,
    Output
}

public partial class GraphNodeViewModel : ObservableObject
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

    [ObservableProperty]
    private bool _isRoot;

    public string Id => Node.Id;
    public string KindLabel => NodeKind switch
    {
        GraphNodeKind.LinearGroup => "线性组",
        GraphNodeKind.ChoiceBranch => "选项分支",
        GraphNodeKind.ConditionBranch => "条件分支",
        _ => NodeKind.ToString()
    };

    public ObservableCollection<GraphConnectorViewModel> InputConnectors { get; } = [];
    public ObservableCollection<GraphConnectorViewModel> OutputConnectors { get; } = [];
    public ObservableCollection<EntryEditorItemViewModel> Entries { get; } = [];
    public ObservableCollection<BranchOptionEditorItemViewModel> Options { get; } = [];
    public ObservableCollection<BranchConditionEditorItemViewModel> Conditions { get; } = [];

    public int EntryCount => Entries.Count;

    public GraphNodeViewModel(Node node, GraphNodeKind nodeKind)
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

        InputConnectors.Add(new GraphConnectorViewModel(this, GraphConnectorKind.Input, 0));

        var outputCount = NodeKind switch
        {
            GraphNodeKind.ChoiceBranch => Math.Max(1, Options.Count),
            GraphNodeKind.ConditionBranch => Math.Max(1, Conditions.Count),
            _ => 1
        };

        for (var i = 0; i < outputCount; i++)
            OutputConnectors.Add(new GraphConnectorViewModel(this, GraphConnectorKind.Output, i));

        OnPropertyChanged(nameof(EntryCount));
    }

    partial void OnNameChanged(string value)
    {
        Node.Name = value;
    }
}

public sealed class GraphConnectorViewModel
{
    public GraphNodeViewModel Node { get; }
    public GraphConnectorKind Kind { get; }
    public int Index { get; }

    public GraphConnectorViewModel(GraphNodeViewModel node, GraphConnectorKind kind, int index)
    {
        Node = node;
        Kind = kind;
        Index = index;
    }
}

public partial class GraphEdgeViewModel : ObservableObject
{
    public GraphNodeViewModel From { get; }
    public GraphNodeViewModel To { get; }
    public int Outlet { get; }

    public double StartX => From.X + 188;
    public double StartY => From.Y + 48 + Outlet * 22;
    public double EndX => To.X - 8;
    public double EndY => To.Y + 48;
    public double ControlOffset => Math.Max(80, Math.Abs(EndX - StartX) * 0.5);
    public string PathData => $"M {StartX},{StartY} C {StartX + ControlOffset},{StartY} {EndX - ControlOffset},{EndY} {EndX},{EndY}";

    public GraphEdgeViewModel(GraphNodeViewModel from, GraphNodeViewModel to, int outlet = 0)
    {
        From = from;
        To = to;
        Outlet = outlet;

        From.PropertyChanged += (_, e) => OnNodeMoved(e.PropertyName);
        To.PropertyChanged += (_, e) => OnNodeMoved(e.PropertyName);
        From.OutputConnectors.CollectionChanged += (_, _) => OnNodeMoved(nameof(GraphNodeViewModel.Y));
        To.InputConnectors.CollectionChanged += (_, _) => OnNodeMoved(nameof(GraphNodeViewModel.Y));
    }

    private void OnNodeMoved(string? propertyName)
    {
        if (propertyName is not (nameof(GraphNodeViewModel.X) or nameof(GraphNodeViewModel.Y)))
            return;

        OnPropertyChanged(nameof(StartX));
        OnPropertyChanged(nameof(StartY));
        OnPropertyChanged(nameof(EndX));
        OnPropertyChanged(nameof(EndY));
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
