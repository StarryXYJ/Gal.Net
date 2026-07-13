using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.Settings;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Project;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Controls;
using GalNet.Editor.Dock;
using GalNet.Editor.Services;
using GalNet.Editor.Models;
using Serilog;

namespace GalNet.Editor.ViewModels;

public partial class EditorWorkspaceViewModel : ObservableObject
{
    private readonly IProjectService _projectService;
    private readonly EditorDockFactory _dockFactory;
    private readonly IEditorDocumentRepository _documentRepository;
    private readonly IGraphEditingService _graphEditingService;
    private readonly IEditorSettingsService _editorSettings;
    private CancellationTokenSource? _autoSaveCts;
    public event Action? VariableDefinitionsChanged;

    [ObservableProperty]
    private GraphNode? _selectedNode;

    [ObservableProperty]
    private GraphEdge? _selectedEdge;

    [ObservableProperty]
    private GamePreviewPanelViewModel? _activePreview;

    [ObservableProperty]
    private AssetEntry? _selectedAsset;

    [ObservableProperty]
    private string _statusText = "Ready";

    public ObservableCollection<GraphNode> Nodes { get; } = [];
    public ObservableCollection<GraphEdge> Edges { get; } = [];
    public ObservableCollection<GraphNode> SelectedNodes { get; } = [];
    public GraphViewportState GraphViewport => _projectService.Current?.EditorState.GraphViewport ?? _fallbackViewport;
    public bool HasMultipleNodeSelection => SelectedNodes.Count > 1;

    private readonly GraphViewportState _fallbackViewport = new();
    private bool _isLoadingGraph;

    public EditorWorkspaceViewModel(
        IProjectService projectService,
        EditorDockFactory dockFactory,
        IEditorDocumentRepository documentRepository,
        IEditorDocumentService documentService,
        IEditorSaveCoordinator saveCoordinator,
        IVariableDefinitionService variableDefinitionService,
        IGraphEditingService graphEditingService,
        IEditorSettingsService editorSettings)
    {
        _projectService = projectService;
        _dockFactory = dockFactory;
        _documentRepository = documentRepository;
        _documentService = documentService;
        _saveCoordinator = saveCoordinator;
        _graphEditingService = graphEditingService;
        _editorSettings = editorSettings;
        _projectService.CurrentChanged += _ => LoadCurrentProjectGraph();
        _documentService.DirtyStateChanged += OnDocumentDirtyStateChanged;
        variableDefinitionService.DefinitionsChanged += _ =>
        {
            OnPropertyChanged(nameof(AllProjectVariableDefinitions));
            VariableDefinitionsChanged?.Invoke();
        };
        LoadCurrentProjectGraph();
    }

    public void SelectNode(GraphNode? node, bool additive = false)
    {
        if (!additive)
            ClearSelection();

        if (node is not null && !SelectedNodes.Contains(node))
            SelectedNodes.Add(node);

        foreach (var selected in SelectedNodes)
            selected.IsSelected = true;

        SelectedNode = SelectedNodes.Count == 1 ? SelectedNodes[0] : null;
        OnPropertyChanged(nameof(HasMultipleNodeSelection));
    }

    public void SelectNodes(IEnumerable<GraphNode> nodes)
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
        OnPropertyChanged(nameof(HasMultipleNodeSelection));
    }

    public void SelectEdge(GraphEdge? edge)
    {
        ClearSelection();
        SelectedEdge = edge;
        if (SelectedEdge is not null)
            SelectedEdge.IsSelected = true;
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

    public void DeleteEdge(GraphEdge edge)
    {
        ExecuteGraphChange(
            () => _graphEditingService.DeleteEdge(Nodes, Edges, edge),
            () =>
            {
                if (ReferenceEquals(SelectedEdge, edge))
                    SelectedEdge = null;
            },
            () => Log.Information("Edge deleted: {From} [{Outlet}] -> {To}", edge.From.Name, edge.Outlet, edge.To.Name));
    }

    public void SaveGraphViewport()
    {
        if (_projectService.Current is not { } project)
            return;

        _ = _projectService.SaveAsync();
    }

    public GraphNode AddNode(GraphNodeKind kind, double x, double y)
    {
        var vm = _graphEditingService.CreateNode(Nodes, kind, x, y);
        TrackNode(vm);
        Nodes.Add(vm);
        UpdateConnectorStates();
        SelectNode(vm);
        Log.Information("Node created: {NodeName} ({NodeKind})", vm.Name, vm.NodeKind);
        MarkGraphDirty();
        return vm;
    }

    public void DeleteNode(GraphNode node)
    {
        ExecuteGraphChange(
            () => _graphEditingService.DeleteNode(Nodes, Edges, node),
            () =>
            {
                SelectedNodes.Remove(node);
                node.IsSelected = false;
                if (ReferenceEquals(SelectedNode, node))
                    SelectedNode = SelectedNodes.Count == 1 ? SelectedNodes[0] : null;
                OnPropertyChanged(nameof(HasMultipleNodeSelection));

                if (node.NodeKind == GraphNodeKind.LinearGroup)
                    _dockFactory.CloseGroupEditor(node.Id);
            },
            () => Log.Information("Node deleted: {NodeName} ({NodeKind})", node.Name, node.NodeKind));
    }

    public void OpenGroupEditor(GraphNode node)
    {
        if (node.NodeKind != GraphNodeKind.LinearGroup)
            return;

        _dockFactory.OpenGroupEditor(node);
        Log.Information("Group editor opened: {NodeName}", node.Name);
    }

    public void Connect(GraphConnector first, GraphConnector second)
    {
        var output = first.Kind == GraphConnectorKind.Output ? first : second;
        var input = first.Kind == GraphConnectorKind.Input ? first : second;
        ExecuteGraphChange(
            () => _graphEditingService.Connect(Nodes, Edges, first, second),
            onSucceeded: null,
            onLogged: () => Log.Information("Nodes connected: {From} [{Outlet}] -> {To}", output.Node.Name, output.Index, input.Node.Name));
    }

    [RelayCommand]
    private void AddChoiceOption()
    {
        AddChoiceOptionTo(SelectedNode);
    }

    public void AddChoiceOptionTo(GraphNode? node)
    {
        if (node?.NodeKind != GraphNodeKind.ChoiceBranch) return;
        ExecuteGraphChange(
            () => _graphEditingService.AddChoiceOption(Nodes, Edges, node),
            onLogged: () => Log.Information("Choice option added: {NodeName}", node.Name));
    }

    [RelayCommand]
    private void RemoveChoiceOption(BranchOptionEditorItemViewModel? option)
    {
        RemoveChoiceOptionFrom(SelectedNode, option);
    }

    public void RemoveChoiceOptionFrom(GraphNode? node, BranchOptionEditorItemViewModel? option)
    {
        if (node?.NodeKind != GraphNodeKind.ChoiceBranch || option is null) return;
        ExecuteGraphChange(() => _graphEditingService.RemoveChoiceOption(Nodes, Edges, node, option));
    }

    public void MoveChoiceOptionTo(BranchOptionEditorItemViewModel? option, int newIndex)
    {
        MoveChoiceOptionTo(SelectedNode, option, newIndex);
    }

    public void MoveChoiceOptionTo(GraphNode? node, BranchOptionEditorItemViewModel? option, int newIndex)
    {
        if (node?.NodeKind != GraphNodeKind.ChoiceBranch || option is null) return;
        ExecuteGraphChange(() => _graphEditingService.MoveChoiceOption(Nodes, Edges, node, option, newIndex));
    }

    public void FocusAsset(AssetEntry asset)
    {
        ClearSelection();
        SelectedAsset = asset;
    }

    [RelayCommand]
    private void ReorderChoiceOption(ReorderRequest? request)
    {
        if (request?.Item is BranchOptionEditorItemViewModel option)
            MoveChoiceOptionTo(option, request.NewIndex);
    }

    [RelayCommand]
    private void AddCondition()
    {
        AddConditionTo(SelectedNode);
    }

    public void AddConditionTo(GraphNode? node)
    {
        if (node?.NodeKind != GraphNodeKind.ConditionBranch) return;
        ExecuteGraphChange(
            () => _graphEditingService.AddCondition(Nodes, Edges, node),
            onLogged: () => Log.Information("Condition added: {NodeName}", node.Name));
    }

    [RelayCommand]
    private void RemoveCondition(BranchConditionEditorItemViewModel? condition)
    {
        RemoveConditionFrom(SelectedNode, condition);
    }

    public void RemoveConditionFrom(GraphNode? node, BranchConditionEditorItemViewModel? condition)
    {
        if (node?.NodeKind != GraphNodeKind.ConditionBranch || condition is null) return;
        ExecuteGraphChange(() => _graphEditingService.RemoveCondition(Nodes, Edges, node, condition));
    }

    public void MoveConditionTo(BranchConditionEditorItemViewModel? condition, int newIndex)
    {
        MoveConditionTo(SelectedNode, condition, newIndex);
    }

    public void MoveConditionTo(GraphNode? node, BranchConditionEditorItemViewModel? condition, int newIndex)
    {
        if (node?.NodeKind != GraphNodeKind.ConditionBranch || condition is null) return;
        ExecuteGraphChange(() => _graphEditingService.MoveCondition(Nodes, Edges, node, condition, newIndex));
    }

    [RelayCommand]
    private void ReorderCondition(ReorderRequest? request)
    {
        if (request?.Item is BranchConditionEditorItemViewModel condition)
            MoveConditionTo(condition, request.NewIndex);
    }

    public void SaveGraphDocument()
    {
        MarkGraphDirty();
    }

    private static EditorGraphNodeDto ToNodeDto(GraphNode node)
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

    public void PersistGraphDocument()
    {
        if (_projectService.Current is not { } project)
            return;

        var document = CreateGraphDocument(project.Name);
        _saveCoordinator.SaveProjectDocument(project.RootPath, document, CreateGroupEntriesSnapshot());
        _documentService.CurrentDocument.Name = project.Name;
        _documentService.MarkSaved();
        project.IsDirty = false;
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

    private void UpdateConnectorStates() => _graphEditingService.UpdateConnectorStates(Nodes, Edges);

    private void ExecuteSelectedNodeGraphChange(
        Func<GraphNode, bool> change,
        Action<GraphNode>? onLogged = null)
    {
        if (SelectedNode is null)
            return;

        ExecuteGraphChange(
            () => change(SelectedNode),
            onSucceeded: null,
            onLogged: onLogged is null ? null : () => onLogged(SelectedNode));
    }

    private void ExecuteGraphChange(
        Func<bool> change,
        Action? onSucceeded = null,
        Action? onLogged = null)
    {
        if (!change())
            return;

        onSucceeded?.Invoke();
        onLogged?.Invoke();
        MarkGraphDirty();
    }

    private void LoadCurrentProjectGraph()
    {
        ClearSelection();
        Nodes.Clear();
        Edges.Clear();

        if (_projectService.Current is not { } project)
        {
            _documentService.Unload();
            BuildSampleGraph();
            return;
        }

        try
        {
            _isLoadingGraph = true;
            var loaded = _documentRepository.Load(project.RootPath, project.Name, project.Settings);
            var document = loaded.Document;
            if (document is null || document.Nodes.Count == 0)
            {
                _documentService.Unload();
                BuildSampleGraph();
                return;
            }

            _documentService.Load(loaded);

            var nodeMap = new Dictionary<string, GraphNode>();

            foreach (var dto in document.Nodes)
            {
                var vm = CreateNodeFromDto(dto, loaded.GroupEntries);
                vm.IsRoot = vm.NodeKind == GraphNodeKind.Entry;
                TrackNode(vm);
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

                Edges.Add(new GraphEdge(from, to, edge.FromOutlet));
            }

            UpdateConnectorStates();
            SelectNode(EntryNode ?? Nodes.FirstOrDefault());
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load graph document; using sample graph");
            _documentService.Unload();
            ClearSelection();
            Nodes.Clear();
            Edges.Clear();
            BuildSampleGraph();
        }
        finally
        {
            _isLoadingGraph = false;
        }
    }

    private GraphNode CreateNodeFromDto(EditorGraphNodeDto dto, IReadOnlyDictionary<string, List<EditorEntryData>> groupEntries)
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

        var vm = new GraphNode(node, kind)
        {
            X = dto.X,
            Y = dto.Y
        };

        if (kind == GraphNodeKind.LinearGroup)
            LoadGroupEntries(vm, groupEntries.TryGetValue(dto.Id, out var entries) ? entries : null);

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

    private void LoadGroupEntries(GraphNode group, IReadOnlyList<EditorEntryData>? entries)
    {
        group.Entries.Clear();
        if (entries is null)
            return;

        foreach (var entry in entries)
            group.Entries.Add(new EntryEditorItemViewModel
            {
                Id = group.Entries.Count + 1,
                Type = entry.Type,
                Condition = entry.Condition,
                Parameters = entry.Parameters
            });
    }

    private void EnsureEntryNode(EditorGraphDocument document, Dictionary<string, GraphNode> nodeMap)
    {
        if (EntryNode is not null)
            return;

        var root = !string.IsNullOrWhiteSpace(document.RootNodeId) && nodeMap.TryGetValue(document.RootNodeId, out var rootNode)
            ? rootNode
            : Nodes.FirstOrDefault();

        var entry = new GraphNode(new Group { Name = "Entry" }, GraphNodeKind.Entry)
        {
            X = root is null ? 4420 : Math.Max(0, root.X - 280),
            Y = root?.Y ?? 4900,
            IsRoot = true
        };

        TrackNode(entry);
        Nodes.Insert(0, entry);
        nodeMap[entry.Id] = entry;

        if (root is not null && !ReferenceEquals(root, entry))
            Edges.Add(new GraphEdge(entry, root));
    }

    private void BuildSampleGraph()
    {
        var entry = new GraphNode(new Group { Name = "Entry" }, GraphNodeKind.Entry)
        {
            X = 4420,
            Y = 4900,
            IsRoot = true
        };

        var opening = new GraphNode(new Group { Name = "Opening" }, GraphNodeKind.LinearGroup)
        {
            X = 4700,
            Y = 4900
        };

        var choice = new GraphNode(new Branch
        {
            Name = "First Choice",
            BranchType = BranchType.Choice
        }, GraphNodeKind.ChoiceBranch)
        {
            X = 5000,
            Y = 4940
        };

        var routeA = new GraphNode(new Group { Name = "Route A" }, GraphNodeKind.LinearGroup)
        {
            X = 5300,
            Y = 4860
        };

        var routeB = new GraphNode(new Group { Name = "Route B" }, GraphNodeKind.LinearGroup)
        {
            X = 5300,
            Y = 5040
        };

        choice.Options.Add(new BranchOptionEditorItemViewModel { Text = "Go Route A" });
        choice.Options.Add(new BranchOptionEditorItemViewModel { Text = "Go Route B" });
        choice.RefreshConnectors();

        Nodes.Add(entry);
        TrackNode(entry);
        Nodes.Add(opening);
        TrackNode(opening);
        Nodes.Add(choice);
        TrackNode(choice);
        Nodes.Add(routeA);
        TrackNode(routeA);
        Nodes.Add(routeB);
        TrackNode(routeB);

        Edges.Add(new GraphEdge(entry, opening));
        Edges.Add(new GraphEdge(opening, choice));
        Edges.Add(new GraphEdge(choice, routeA, 0));
        Edges.Add(new GraphEdge(choice, routeB, 1));
        UpdateConnectorStates();

        SelectNode(entry);
    }

    private void MarkGraphDirty()
    {
        _documentService.MarkDirty();
        if (_projectService.Current is { } project)
            project.IsDirty = true;
        ScheduleAutoSave();
    }

    private void ScheduleAutoSave()
    {
        _autoSaveCts?.Cancel(); _autoSaveCts?.Dispose();
        var cts = _autoSaveCts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(500, cts.Token);
                if (!cts.IsCancellationRequested)
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        PersistGraphDocument();
                        _ = _projectService.SaveAsync();
                        StatusText = "Auto-saved";
                    });
            }
            catch (OperationCanceledException) { }
        });
    }

    private void OnDocumentDirtyStateChanged(bool isDirty)
    {
        if (_projectService.Current is { } project)
            project.IsDirty = isDirty;
    }

    private EditorGraphDocument CreateGraphDocument(string projectName) =>
        new()
        {
            Version = _documentService.CurrentDocument.Version <= 0 ? 2 : _documentService.CurrentDocument.Version,
            Name = projectName,
            RootNodeId = EntryNode?.Id ?? Nodes.FirstOrDefault()?.Id ?? "",
            Nodes = Nodes.Select(ToNodeDto).ToList(),
            Edges = Edges.Select(e => new EditorGraphEdgeDto
            {
                FromNodeId = e.From.Id,
                FromOutlet = e.Outlet,
                ToNodeId = e.To.Id
            }).ToList(),
            PlayerVariables = _documentService.CurrentDocument.PlayerVariables.Select(v => v.Clone()).ToList(),
            SaveVariables = _documentService.CurrentDocument.SaveVariables.Select(v => v.Clone()).ToList()
        };

    private IReadOnlyDictionary<string, IReadOnlyList<EditorEntryData>> CreateGroupEntriesSnapshot() =>
        Nodes.Where(n => n.NodeKind == GraphNodeKind.LinearGroup)
            .ToDictionary(
                node => node.Id,
                node => (IReadOnlyList<EditorEntryData>)node.Entries.Select(entry => new EditorEntryData
                {
                    Id = entry.Id,
                    Type = entry.Type,
                    Condition = entry.Condition,
                    Parameters = entry.Parameters
                }).ToList());

    private void TrackNode(GraphNode node)
    {
        node.PropertyChanged += OnNodePropertyChanged;
        TrackCollection(node.Entries, OnEntryChanged);
        TrackCollection(node.Options, OnBranchOptionChanged);
        TrackCollection(node.Conditions, OnBranchConditionChanged);
    }

    private void TrackCollection<TItem>(
        ObservableCollection<TItem> collection,
        PropertyChangedEventHandler itemHandler)
        where TItem : ObservableObject
    {
        collection.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
            {
                foreach (var item in e.NewItems.OfType<TItem>())
                    item.PropertyChanged += itemHandler;
            }

            if (e.OldItems is not null)
            {
                foreach (var item in e.OldItems.OfType<TItem>())
                    item.PropertyChanged -= itemHandler;
            }

            if (!_isLoadingGraph && e.Action != NotifyCollectionChangedAction.Reset)
                MarkGraphDirty();
        };

        foreach (var item in collection)
            item.PropertyChanged += itemHandler;
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isLoadingGraph)
            return;

        if (e.PropertyName is nameof(GraphNode.Name))
            MarkGraphDirty();
    }

    private void OnEntryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_isLoadingGraph && e.PropertyName is nameof(EntryEditorItemViewModel.Type)
            or nameof(EntryEditorItemViewModel.Condition)
            or nameof(EntryEditorItemViewModel.Parameters))
            MarkGraphDirty();
    }

    private void OnBranchOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_isLoadingGraph && e.PropertyName is nameof(BranchOptionEditorItemViewModel.Text)
            or nameof(BranchOptionEditorItemViewModel.Condition))
            MarkGraphDirty();
    }

    private void OnBranchConditionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_isLoadingGraph && e.PropertyName == nameof(BranchConditionEditorItemViewModel.Expression))
            MarkGraphDirty();
    }

    public GraphNode? EntryNode => Nodes.FirstOrDefault(n => n.NodeKind == GraphNodeKind.Entry);
}

