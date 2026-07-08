using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.Settings;
using GalNet.Editor.Controls;
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
    private GraphEdgeViewModel? _selectedEdge;

    [ObservableProperty]
    private GamePreviewPanelViewModel? _activePreview;

    [ObservableProperty]
    private InspectorMode _inspectorMode = InspectorMode.Node;

    [ObservableProperty]
    private string _statusText = "Ready";

    public ObservableCollection<GraphNodeViewModel> Nodes { get; } = [];
    public ObservableCollection<GraphEdgeViewModel> Edges { get; } = [];
    public ObservableCollection<GraphNodeViewModel> SelectedNodes { get; } = [];
    public GraphViewportState GraphViewport => _projectService.Current?.EditorState.GraphViewport ?? _fallbackViewport;
    public bool HasMultipleNodeSelection => SelectedNodes.Count > 1;

    private readonly GraphViewportState _fallbackViewport = new();

    public EditorWorkspaceViewModel(
        IProjectService projectService,
        EditorDockFactory dockFactory)
    {
        _projectService = projectService;
        _dockFactory = dockFactory;
        _projectService.CurrentChanged += _ => LoadCurrentProjectGraph();
        LoadCurrentProjectGraph();
    }

    public void SelectNode(GraphNodeViewModel? node, bool additive = false)
    {
        if (!additive)
            ClearSelection();

        if (node is not null && !SelectedNodes.Contains(node))
            SelectedNodes.Add(node);

        foreach (var selected in SelectedNodes)
            selected.IsSelected = true;

        SelectedNode = SelectedNodes.Count == 1 ? SelectedNodes[0] : null;
        InspectorMode = InspectorMode.Node;
        OnPropertyChanged(nameof(HasMultipleNodeSelection));
    }

    public void SelectNodes(IEnumerable<GraphNodeViewModel> nodes)
    {
        ClearSelection();

        foreach (var node in nodes)
        {
            if (SelectedNodes.Contains(node))
                continue;

            SelectedNodes.Add(node);
            node.IsSelected = true;
        }

        SelectedNode = SelectedNodes.Count == 1 ? SelectedNodes[0] : null;
        InspectorMode = InspectorMode.Node;
        OnPropertyChanged(nameof(HasMultipleNodeSelection));
    }

    public void SelectEdge(GraphEdgeViewModel? edge)
    {
        ClearSelection();
        SelectedEdge = edge;
        if (SelectedEdge is not null)
            SelectedEdge.IsSelected = true;
        InspectorMode = InspectorMode.Node;
    }

    public void ClearSelection()
    {
        foreach (var node in SelectedNodes)
            node.IsSelected = false;

        SelectedNodes.Clear();

        if (SelectedEdge is not null)
            SelectedEdge.IsSelected = false;

        SelectedNode = null;
        SelectedEdge = null;
        OnPropertyChanged(nameof(HasMultipleNodeSelection));
    }

    public void DeleteSelection()
    {
        if (SelectedEdge is not null)
        {
            DeleteEdge(SelectedEdge);
            return;
        }

        var nodes = SelectedNodes.Where(n => n.CanDelete).ToList();
        foreach (var node in nodes)
            DeleteNode(node);
    }

    public void DeleteEdge(GraphEdgeViewModel edge)
    {
        if (!Edges.Remove(edge))
            return;

        if (ReferenceEquals(SelectedEdge, edge))
            SelectedEdge = null;

        UpdateConnectorStates();
        Log.Information("Edge deleted: {From} [{Outlet}] -> {To}", edge.From.Name, edge.Outlet, edge.To.Name);
        SaveGraphDocument();
    }

    public void FocusPreview(GamePreviewPanelViewModel preview)
    {
        ClearSelection();
        ActivePreview = preview;
        InspectorMode = InspectorMode.PreviewVariables;
    }

    public void SaveGraphViewport()
    {
        if (_projectService.Current is not { } project)
            return;

        project.IsDirty = true;
        _ = _projectService.SaveAsync();
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
        UpdateConnectorStates();
        SelectNode(vm);
        Log.Information("Node created: {NodeName} ({NodeKind})", vm.Name, vm.NodeKind);
        SaveGraphDocument();
        return vm;
    }

    public void DeleteNode(GraphNodeViewModel node)
    {
        if (!node.CanDelete)
            return;

        var relatedEdges = Edges
            .Where(e => ReferenceEquals(e.From, node) || ReferenceEquals(e.To, node))
            .ToList();

        foreach (var edge in relatedEdges)
            Edges.Remove(edge);

        Nodes.Remove(node);
        UpdateConnectorStates();

        SelectedNodes.Remove(node);
        node.IsSelected = false;
        if (ReferenceEquals(SelectedNode, node))
            SelectedNode = SelectedNodes.Count == 1 ? SelectedNodes[0] : null;
        OnPropertyChanged(nameof(HasMultipleNodeSelection));

        if (node.NodeKind == GraphNodeKind.LinearGroup)
            _dockFactory.CloseGroupEditor(node.Id);

        Log.Information("Node deleted: {NodeName} ({NodeKind})", node.Name, node.NodeKind);
        SaveGraphDocument();
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

        var conflictingEdges = Edges
            .Where(e =>
                ReferenceEquals(e.To, input.Node)
                || (ReferenceEquals(e.From, output.Node) && e.Outlet == output.Index))
            .ToList();

        foreach (var edge in conflictingEdges)
            Edges.Remove(edge);

        Edges.Add(new GraphEdgeViewModel(output.Node, input.Node, output.Index));
        UpdateConnectorStates();
        Log.Information("Nodes connected: {From} [{Outlet}] -> {To}", output.Node.Name, output.Index, input.Node.Name);
        SaveGraphDocument();
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
        UpdateConnectorStates();
        Log.Information("Choice option added: {NodeName}", SelectedNode.Name);
        SaveGraphDocument();
    }

    [RelayCommand]
    private void RemoveChoiceOption(BranchOptionEditorItemViewModel? option)
    {
        if (SelectedNode is null || option is null)
            return;

        SelectedNode.Options.Remove(option);
        SelectedNode.RefreshConnectors();
        RemoveDanglingOutletEdges(SelectedNode);
        UpdateConnectorStates();
        SaveGraphDocument();
    }

    public void MoveChoiceOptionTo(BranchOptionEditorItemViewModel? option, int newIndex)
    {
        if (SelectedNode?.NodeKind != GraphNodeKind.ChoiceBranch || option is null)
            return;

        MoveBranchItemWithEdges(SelectedNode, SelectedNode.Options, option, newIndex);
        SaveGraphDocument();
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
        UpdateConnectorStates();
        Log.Information("Condition added: {NodeName}", SelectedNode.Name);
        SaveGraphDocument();
    }

    [RelayCommand]
    private void RemoveCondition(BranchConditionEditorItemViewModel? condition)
    {
        if (SelectedNode is null || condition is null)
            return;

        SelectedNode.Conditions.Remove(condition);
        SelectedNode.RefreshConnectors();
        RemoveDanglingOutletEdges(SelectedNode);
        UpdateConnectorStates();
        SaveGraphDocument();
    }

    public void MoveConditionTo(BranchConditionEditorItemViewModel? condition, int newIndex)
    {
        if (SelectedNode?.NodeKind != GraphNodeKind.ConditionBranch || condition is null)
            return;

        MoveBranchItemWithEdges(SelectedNode, SelectedNode.Conditions, condition, newIndex);
        SaveGraphDocument();
    }

    public void SaveGraphDocument()
    {
        if (_projectService.Current is not { } project)
            return;

        Directory.CreateDirectory(project.GraphPath);
        var groupsDir = Path.Combine(project.GraphPath, "groups");
        Directory.CreateDirectory(groupsDir);

        var document = new EditorGraphDocument
        {
            Name = project.Name,
            RootNodeId = EntryNode?.Id ?? Nodes.FirstOrDefault()?.Id ?? "",
            Nodes = Nodes.Select(ToNodeDto).ToList(),
            Edges = Edges.Select(e => new EditorGraphEdgeDto
            {
                FromNodeId = e.From.Id,
                FromOutlet = e.Outlet,
                ToNodeId = e.To.Id
            }).ToList()
        };

        foreach (var group in Nodes.Where(n => n.NodeKind == GraphNodeKind.LinearGroup))
            File.WriteAllLines(
                Path.Combine(groupsDir, $"{group.Id}.galgroup"),
                group.Entries.Select(SerializeEntry));

        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(project.GraphPath, "graph.json"), json);
        project.IsDirty = true;
    }

    private static EditorGraphNodeDto ToNodeDto(GraphNodeViewModel node)
    {
        var dto = new EditorGraphNodeDto
        {
            Id = node.Id,
            Type = node.NodeKind switch
            {
                GraphNodeKind.Entry => "Entry",
                GraphNodeKind.LinearGroup => "Group",
                _ => "Branch"
            },
            Name = node.Name,
            X = node.X,
            Y = node.Y
        };

        if (node.NodeKind == GraphNodeKind.LinearGroup)
        {
            dto.File = $"groups/{node.Id}.galgroup";
        }
        else if (node.NodeKind is GraphNodeKind.ChoiceBranch or GraphNodeKind.ConditionBranch)
        {
            dto.BranchType = node.NodeKind == GraphNodeKind.ChoiceBranch ? "Choice" : "Condition";
            dto.Options = node.NodeKind == GraphNodeKind.ChoiceBranch
                ? node.Options.Select(o => new EditorGraphBranchOptionDto
                {
                    Text = o.Text,
                    Condition = o.Condition
                }).ToList()
                : null;
            dto.Conditions = node.NodeKind == GraphNodeKind.ConditionBranch
                ? node.Conditions.Select(c => new EditorGraphBranchConditionDto
                {
                    Expression = c.Expression
                }).ToList()
                : null;
        }

        return dto;
    }

    private static string SerializeEntry(EntryEditorItemViewModel entry)
    {
        var parameters = entry.Parameters
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]))
            .ToDictionary(parts => parts[0], parts => parts.Length > 1 ? parts[1] : "");

        if (!string.IsNullOrWhiteSpace(entry.Condition))
            parameters["condition"] = entry.Condition;

        return GalNet.Core.Serialization.GalgroupParser.Serialize(entry.Type, parameters);
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

        UpdateConnectorStates();
    }

    private void MoveBranchItemWithEdges<TItem>(
        GraphNodeViewModel node,
        ObservableCollection<TItem> items,
        TItem item,
        int newIndex)
        where TItem : class
    {
        var oldIndex = items.IndexOf(item);
        if (oldIndex < 0 || newIndex < 0 || newIndex >= items.Count || oldIndex == newIndex)
            return;

        var oldOrder = items.ToList();
        var edgeByItem = oldOrder
            .Select((entry, index) => new { entry, edge = Edges.FirstOrDefault(e => ReferenceEquals(e.From, node) && e.Outlet == index) })
            .Where(x => x.edge is not null)
            .ToDictionary(x => x.entry, x => x.edge!);

        items.Move(oldIndex, newIndex);

        for (var i = 0; i < items.Count; i++)
        {
            if (edgeByItem.TryGetValue(items[i], out var edge))
                edge.Outlet = i;
        }

        node.RefreshConnectors();
        UpdateConnectorStates();
    }

    private void UpdateConnectorStates()
    {
        foreach (var connector in Nodes.SelectMany(n => n.InputConnectors.Concat(n.OutputConnectors)))
            connector.IsConnected = false;

        foreach (var edge in Edges)
        {
            var output = edge.From.OutputConnectors.FirstOrDefault(c => c.Index == edge.Outlet);
            if (output is not null)
                output.IsConnected = true;

            var input = edge.To.InputConnectors.FirstOrDefault();
            if (input is not null)
                input.IsConnected = true;
        }
    }

    private void LoadCurrentProjectGraph()
    {
        ClearSelection();
        Nodes.Clear();
        Edges.Clear();

        if (_projectService.Current is not { } project)
        {
            BuildSampleGraph();
            return;
        }

        var graphFile = Path.Combine(project.GraphPath, "graph.json");
        if (!File.Exists(graphFile))
        {
            BuildSampleGraph();
            return;
        }

        try
        {
            var json = File.ReadAllText(graphFile);
            var document = JsonSerializer.Deserialize<EditorGraphDocument>(json);
            if (document is null || document.Nodes.Count == 0)
            {
                BuildSampleGraph();
                return;
            }

            var nodeMap = new Dictionary<string, GraphNodeViewModel>();

            foreach (var dto in document.Nodes)
            {
                var vm = CreateNodeFromDto(dto);
                vm.IsRoot = vm.NodeKind == GraphNodeKind.Entry;
                Nodes.Add(vm);
                nodeMap[vm.Id] = vm;
            }

            EnsureEntryNode(document, nodeMap);

            foreach (var edge in document.Edges)
            {
                if (!nodeMap.TryGetValue(edge.FromNodeId, out var from)
                    || !nodeMap.TryGetValue(edge.ToNodeId, out var to)
                    || edge.FromOutlet < 0
                    || edge.FromOutlet >= from.OutputConnectors.Count
                    || to.InputConnectors.Count == 0)
                    continue;

                Edges.Add(new GraphEdgeViewModel(from, to, edge.FromOutlet));
            }

            UpdateConnectorStates();
            SelectNode(EntryNode ?? Nodes.FirstOrDefault());
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load graph document; using sample graph");
            ClearSelection();
            Nodes.Clear();
            Edges.Clear();
            BuildSampleGraph();
        }
    }

    private GraphNodeViewModel CreateNodeFromDto(EditorGraphNodeDto dto)
    {
        var kind = dto.Type.Equals("Entry", StringComparison.OrdinalIgnoreCase)
            ? GraphNodeKind.Entry
            : dto.Type.Equals("Group", StringComparison.OrdinalIgnoreCase)
                ? GraphNodeKind.LinearGroup
                : dto.BranchType?.Equals("Condition", StringComparison.OrdinalIgnoreCase) == true
                    ? GraphNodeKind.ConditionBranch
                    : GraphNodeKind.ChoiceBranch;

        Node node = kind switch
        {
            GraphNodeKind.Entry => new Group { Id = dto.Id, Name = string.IsNullOrWhiteSpace(dto.Name) ? "Entry" : dto.Name },
            GraphNodeKind.LinearGroup => new Group { Id = dto.Id, Name = dto.Name },
            GraphNodeKind.ChoiceBranch => new Branch { Id = dto.Id, Name = dto.Name, BranchType = BranchType.Choice },
            GraphNodeKind.ConditionBranch => new Branch { Id = dto.Id, Name = dto.Name, BranchType = BranchType.Condition },
            _ => throw new ArgumentOutOfRangeException()
        };

        var vm = new GraphNodeViewModel(node, kind)
        {
            X = dto.X,
            Y = dto.Y
        };

        if (kind == GraphNodeKind.LinearGroup)
            LoadGroupEntries(vm, dto.File);

        if (kind == GraphNodeKind.ChoiceBranch)
        {
            vm.Options.Clear();
            foreach (var option in dto.Options ?? [])
            {
                vm.Options.Add(new BranchOptionEditorItemViewModel
                {
                    Text = option.Text,
                    Condition = option.Condition
                });
            }
        }

        if (kind == GraphNodeKind.ConditionBranch)
        {
            vm.Conditions.Clear();
            foreach (var condition in dto.Conditions ?? [])
                vm.Conditions.Add(new BranchConditionEditorItemViewModel { Expression = condition.Expression });
        }

        vm.RefreshConnectors();
        return vm;
    }

    private void LoadGroupEntries(GraphNodeViewModel group, string? relativeFile)
    {
        group.Entries.Clear();

        if (_projectService.Current is not { } project || string.IsNullOrWhiteSpace(relativeFile))
            return;

        var file = Path.Combine(project.GraphPath, relativeFile.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(file))
            return;

        var parsed = GalNet.Core.Serialization.GalgroupParser.Parse(File.ReadAllText(file));
        foreach (var entry in parsed)
        {
            var parameters = entry.Params
                .Where(p => p.Key != "condition")
                .Select(p => string.IsNullOrEmpty(p.Value) ? p.Key : $"{p.Key}={p.Value}");

            group.Entries.Add(new EntryEditorItemViewModel
            {
                Id = group.Entries.Count + 1,
                Type = entry.EntryType,
                Condition = entry.Params.TryGetValue("condition", out var condition) ? condition : "",
                Parameters = string.Join("; ", parameters)
            });
        }
    }

    private void EnsureEntryNode(EditorGraphDocument document, Dictionary<string, GraphNodeViewModel> nodeMap)
    {
        if (EntryNode is not null)
            return;

        var root = !string.IsNullOrWhiteSpace(document.RootNodeId) && nodeMap.TryGetValue(document.RootNodeId, out var rootNode)
            ? rootNode
            : Nodes.FirstOrDefault();

        var entry = new GraphNodeViewModel(new Group { Name = "Entry" }, GraphNodeKind.Entry)
        {
            X = root is null ? 4420 : Math.Max(0, root.X - 280),
            Y = root?.Y ?? 4900,
            IsRoot = true
        };

        Nodes.Insert(0, entry);
        nodeMap[entry.Id] = entry;

        if (root is not null && !ReferenceEquals(root, entry))
            Edges.Add(new GraphEdgeViewModel(entry, root));
    }

    private void BuildSampleGraph()
    {
        var entry = new GraphNodeViewModel(new Group { Name = "Entry" }, GraphNodeKind.Entry)
        {
            X = 4420,
            Y = 4900,
            IsRoot = true
        };

        var opening = new GraphNodeViewModel(new Group { Name = "Opening" }, GraphNodeKind.LinearGroup)
        {
            X = 4700,
            Y = 4900
        };

        var choice = new GraphNodeViewModel(new Branch
        {
            Name = "First Choice",
            BranchType = BranchType.Choice
        }, GraphNodeKind.ChoiceBranch)
        {
            X = 5000,
            Y = 4940
        };

        var routeA = new GraphNodeViewModel(new Group { Name = "Route A" }, GraphNodeKind.LinearGroup)
        {
            X = 5300,
            Y = 4860
        };

        var routeB = new GraphNodeViewModel(new Group { Name = "Route B" }, GraphNodeKind.LinearGroup)
        {
            X = 5300,
            Y = 5040
        };

        choice.Options.Add(new BranchOptionEditorItemViewModel { Text = "Go Route A" });
        choice.Options.Add(new BranchOptionEditorItemViewModel { Text = "Go Route B" });
        choice.RefreshConnectors();

        Nodes.Add(entry);
        Nodes.Add(opening);
        Nodes.Add(choice);
        Nodes.Add(routeA);
        Nodes.Add(routeB);

        Edges.Add(new GraphEdgeViewModel(entry, opening));
        Edges.Add(new GraphEdgeViewModel(opening, choice));
        Edges.Add(new GraphEdgeViewModel(choice, routeA, 0));
        Edges.Add(new GraphEdgeViewModel(choice, routeB, 1));
        UpdateConnectorStates();

        SelectNode(entry);
    }

    public GraphNodeViewModel? EntryNode => Nodes.FirstOrDefault(n => n.NodeKind == GraphNodeKind.Entry);
}

public enum InspectorMode
{
    Node,
    PreviewVariables
}

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
        GraphNodeKind.Entry => "入口",
        GraphNodeKind.LinearGroup => "线性组",
        GraphNodeKind.ChoiceBranch => "选项分支",
        GraphNodeKind.ConditionBranch => "条件分支",
        _ => NodeKind.ToString()
    };

    public bool IsEntryNode => NodeKind == GraphNodeKind.Entry;
    public bool CanDelete => !IsEntryNode;
    public IBrush NodeBackground => IsSelected ? Brush.Parse("#312A55") : Brush.Parse("#1E1E2E");
    public IBrush NodeBorderBrush => IsSelected ? Brush.Parse("#8F72FF") : IsEntryNode ? Brush.Parse("#A891FF") : Brush.Parse("#45475A");
    public double NodeBorderThickness => IsSelected ? 2 : 1;

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

        if (!IsEntryNode)
            InputConnectors.Add(new GraphConnectorViewModel(this, GraphConnectorKind.Input, 0));

        var outputCount = NodeKind switch
        {
            GraphNodeKind.Entry => 1,
            GraphNodeKind.ChoiceBranch => Math.Max(1, Options.Count),
            GraphNodeKind.ConditionBranch => Math.Max(1, Conditions.Count),
            _ => 1
        };

        for (var i = 0; i < outputCount; i++)
            OutputConnectors.Add(new GraphConnectorViewModel(this, GraphConnectorKind.Output, i));

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

public partial class GraphConnectorViewModel : ObservableObject
{
    public GraphNodeViewModel Node { get; }
    public GraphConnectorKind Kind { get; }
    public int Index { get; }

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isPreviewTarget;

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

    public GraphEdgeViewModel(GraphNodeViewModel from, GraphNodeViewModel to, int outlet = 0)
    {
        From = from;
        To = to;
        _outlet = outlet;

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
