using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
using GalNet.Editor.Services.Interfaces;
using GalNet.Editor.Models;
using Serilog;

namespace GalNet.Editor.ViewModels;

public partial class EditorWorkspaceViewModel : ObservableObject, IDisposable
{
    private readonly IProjectService _projectService;
    private readonly EditorDockFactory _dockFactory;
    private readonly IEditorDocumentRepository _documentRepository;
    private readonly IGraphEditingService _graphEditingService;
    private readonly GraphDocumentMapper _graphDocumentMapper;
    private readonly IEditorSettingsService _editorSettings;
    private readonly IVariableDefinitionService _variableDefinitionService;
    private readonly Action<GalProject?> _projectChangedHandler;
    private readonly Action<GalNet.Core.Variable.VariableScope> _definitionsChangedHandler;
    private readonly IProjectSaveScheduler _saveScheduler;
    private readonly GraphChangeTracker _graphChangeTracker;
    private bool _disposed;
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
        IEditorSettingsService editorSettings,
        IProjectSaveScheduler saveScheduler,
        GraphDocumentMapper graphDocumentMapper)
    {
        _projectService = projectService;
        _dockFactory = dockFactory;
        _documentRepository = documentRepository;
        _documentService = documentService;
        _saveCoordinator = saveCoordinator;
        _graphEditingService = graphEditingService;
        _editorSettings = editorSettings;
        _saveScheduler = saveScheduler;
        _graphDocumentMapper = graphDocumentMapper;
        _variableDefinitionService = variableDefinitionService;
        _projectChangedHandler = _ => LoadCurrentProjectGraph();
        _definitionsChangedHandler = _ =>
        {
            OnPropertyChanged(nameof(AllProjectVariableDefinitions));
            VariableDefinitionsChanged?.Invoke();
        };
        _projectService.CurrentChanged += _projectChangedHandler;
        _documentService.DirtyStateChanged += OnDocumentDirtyStateChanged;
        _variableDefinitionService.DefinitionsChanged += _definitionsChangedHandler;
        _graphChangeTracker = new GraphChangeTracker(MarkGraphDirty, () => _isLoadingGraph);
        LoadCurrentProjectGraph();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _projectService.CurrentChanged -= _projectChangedHandler;
        _documentService.DirtyStateChanged -= OnDocumentDirtyStateChanged;
        _variableDefinitionService.DefinitionsChanged -= _definitionsChangedHandler;
        _graphChangeTracker.Dispose();
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
        if (_projectService.Current is null)
            return;

        _saveScheduler.Schedule(_projectService.SaveAsync);
    }

    public GraphNode AddNode(GraphNodeKind kind, double x, double y)
    {
        var vm = _graphEditingService.CreateNode(Nodes, kind, x, y);
        _graphChangeTracker.Track(vm);
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

    public void PersistGraphDocument()
    {
        if (_projectService.Current is not { } project)
            return;

        var document = _graphDocumentMapper.CreateDocument(
            project.Name,
            _documentService.CurrentDocument.Version,
            Nodes,
            Edges,
            _documentService.CurrentDocument.PlayerVariables,
            _documentService.CurrentDocument.SaveVariables);
        _saveCoordinator.SaveProjectDocument(project.RootPath, document, _graphDocumentMapper.CreateGroupEntriesSnapshot(Nodes));
        _documentService.CurrentDocument.Name = project.Name;
    }

    public Task SaveAsync() => _saveScheduler.SaveNowAsync(SaveCoreAsync);

    private async Task SaveCoreAsync()
    {
        if (_projectService.Current is null)
            return;

        PersistGraphDocument();
        await _projectService.SaveAsync();
        _documentService.MarkSaved();
        if (_projectService.Current is { } project)
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
        _graphChangeTracker.Clear();
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

            var graph = _graphDocumentMapper.Load(loaded);
            foreach (var node in graph.Nodes)
            {
                node.IsRoot = node.NodeKind == GraphNodeKind.Entry;
                _graphChangeTracker.Track(node);
                Nodes.Add(node);
            }
            foreach (var edge in graph.Edges)
                Edges.Add(edge);

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
        _graphChangeTracker.Track(entry);
        Nodes.Add(opening);
        _graphChangeTracker.Track(opening);
        Nodes.Add(choice);
        _graphChangeTracker.Track(choice);
        Nodes.Add(routeA);
        _graphChangeTracker.Track(routeA);
        Nodes.Add(routeB);
        _graphChangeTracker.Track(routeB);

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
        if (!_editorSettings.GetSettings().AutoSaveProject)
            return;

        _saveScheduler.Schedule(async () =>
        {
            await SaveCoreAsync();
            Avalonia.Threading.Dispatcher.UIThread.Post(() => StatusText = "Auto-saved");
        });
    }

    private void OnDocumentDirtyStateChanged(bool isDirty)
    {
        if (_projectService.Current is { } project)
            project.IsDirty = isDirty;
    }

    public GraphNode? EntryNode => Nodes.FirstOrDefault(n => n.NodeKind == GraphNodeKind.Entry);
}

