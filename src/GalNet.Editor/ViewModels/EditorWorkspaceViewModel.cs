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
using GalNet.Editor.History;
using Serilog;

namespace GalNet.Editor.ViewModels;

public partial class EditorWorkspaceViewModel : ObservableObject, IDisposable, IUndoRedoTarget
{
    private readonly IProjectService _projectService;
    private readonly IEditorDocumentRepository _documentRepository;
    private readonly EditorHistories _histories;
    private readonly EditorDockFactory _dockFactory;
    private readonly IGraphEditingService _graphEditingService;
    private readonly GraphDocumentMapper _graphDocumentMapper;
    private readonly IEditorSettingsService _editorSettings;
    private readonly IVariableDefinitionService _variableDefinitionService;
    private readonly Action<GalProject?> _projectChangedHandler;
    private readonly Action<GalNet.Core.Variable.VariableScope> _definitionsChangedHandler;
    private readonly IProjectSaveScheduler _saveScheduler;
    private readonly GraphChangeTracker _graphChangeTracker;
    private bool _disposed;
    private bool _isSaving;
    private bool _isApplyingHistory;
    private readonly Dictionary<string, (double X, double Y)> _savedPositions = [];
    private readonly Dictionary<(object Item, string Property), object?> _propertyValues = [];
    private readonly Dictionary<GalNet.Core.Variable.VariableScope, List<GalNet.Core.Variable.ProjectVariableDefinition>> _variableSnapshots = [];
    public IUndoRedoHistory UndoRedoHistory => _histories.Graph;
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
        IEditorDocumentRepository documentRepository,
        EditorHistories histories,
        EditorDockFactory dockFactory,
        IEditorDocumentService documentService,
        IEditorSaveCoordinator saveCoordinator,
        IVariableDefinitionService variableDefinitionService,
        IGraphEditingService graphEditingService,
        IEditorSettingsService editorSettings,
        IProjectSaveScheduler saveScheduler,
        GraphDocumentMapper graphDocumentMapper)
    {
        _projectService = projectService;
        _documentRepository = documentRepository;
        _histories = histories;
        _dockFactory = dockFactory;
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
            RecordVariableDefinitionsChange(_);
            OnPropertyChanged(nameof(AllProjectVariableDefinitions));
            VariableDefinitionsChanged?.Invoke();
        };
        _projectService.CurrentChanged += _projectChangedHandler;
        _documentService.DirtyStateChanged += OnDocumentDirtyStateChanged;
        _variableDefinitionService.DefinitionsChanged += _definitionsChangedHandler;
        _histories.Changed += OnHistoryChanged;
        _graphChangeTracker = new GraphChangeTracker(
            MarkGraphDirty,
            () => _isLoadingGraph,
            OnTrackedNodeChanged,
            OnTrackedItemChanged);
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
        _histories.Changed -= OnHistoryChanged;
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
        var index = Edges.IndexOf(edge);
        if (index < 0) return;
        ExecuteGraphEdit(new DelegateEdit("Delete edge",
            () => { Edges.Insert(Math.Min(index, Edges.Count), edge); UpdateConnectorStates(); },
            () => { Edges.Remove(edge); UpdateConnectorStates(); }));
    }

    public void SaveGraphViewport()
    {
        if (_projectService.Current is null)
            return;

        _saveScheduler.Schedule(_projectService.SaveAsync);
    }

    public GraphNode AddNode(GraphNodeKind kind, double x, double y)
    {
        var id = Guid.NewGuid().ToString("N");
        var index = Nodes.Count(node => node.NodeKind == kind) + 1;
        var name = kind switch
        {
            GraphNodeKind.LinearGroup => $"Linear Group {index}",
            GraphNodeKind.ChoiceBranch => $"Choice Branch {index}",
            GraphNodeKind.ConditionBranch => $"Condition Branch {index}",
            _ => "Entry"
        };
        var node = _graphEditingService.CreateNode(Nodes, kind, x, y, id);
        node.Name = name;
        TrackNode(node);
        ExecuteGraphEdit(new DelegateEdit("Add node",
            () => { Nodes.Remove(node); UpdateConnectorStates(); },
            () => { if (!Nodes.Contains(node)) Nodes.Add(node); UpdateConnectorStates(); }));
        SelectNode(node);
        return node;
    }

    public void DeleteNode(GraphNode node)
    {
        if (!node.CanDelete || !Nodes.Contains(node)) return;
        var nodeIndex = Nodes.IndexOf(node);
        var related = Edges.Select((edge, index) => (edge, index))
            .Where(pair => ReferenceEquals(pair.edge.From, node) || ReferenceEquals(pair.edge.To, node)).ToList();
        ExecuteGraphEdit(new DelegateEdit("Delete node",
            () =>
            {
                Nodes.Insert(Math.Min(nodeIndex, Nodes.Count), node);
                foreach (var (edge, index) in related.OrderBy(pair => pair.index))
                    Edges.Insert(Math.Min(index, Edges.Count), edge);
                UpdateConnectorStates();
            },
            () =>
            {
                foreach (var (edge, _) in related) Edges.Remove(edge);
                Nodes.Remove(node); UpdateConnectorStates();
            }));
        if (node.NodeKind == GraphNodeKind.LinearGroup)
            _dockFactory.CloseGroupEditor(node.Id);
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
        if (ReferenceEquals(output.Node, input.Node) || output.Kind != GraphConnectorKind.Output || input.Kind != GraphConnectorKind.Input) return;
        var before = Edges.ToList();
        if (!_graphEditingService.Connect(Nodes, Edges, output, input)) return;
        var after = Edges.ToList();
        PushGraphEdit(new DelegateEdit("Connect nodes", () => ReplaceEdges(before), () => ReplaceEdges(after)));
    }

    [RelayCommand]
    private void AddChoiceOption()
    {
        AddChoiceOptionTo(SelectedNode);
    }

    public void AddChoiceOptionTo(GraphNode? node)
    {
        if (node?.NodeKind != GraphNodeKind.ChoiceBranch) return;
        if (!_graphEditingService.AddChoiceOption(Nodes, Edges, node)) return;
        var option = node.Options[^1];
        _propertyValues[(option, nameof(option.Text))] = option.Text;
        _propertyValues[(option, nameof(option.Condition))] = option.Condition;
        PushGraphEdit(new DelegateEdit("Add choice option",
            () => { node.Options.Remove(option); RefreshGraphNode(node); },
            () => { node.Options.Add(option); RefreshGraphNode(node); }));
    }

    [RelayCommand]
    private void RemoveChoiceOption(BranchOptionEditorItemViewModel? option)
    {
        RemoveChoiceOptionFrom(SelectedNode, option);
    }

    public void RemoveChoiceOptionFrom(GraphNode? node, BranchOptionEditorItemViewModel? option)
    {
        if (node?.NodeKind != GraphNodeKind.ChoiceBranch || option is null) return;
        var index = node.Options.IndexOf(option); if (index < 0) return;
        var edges = Edges.ToList();
        if (!_graphEditingService.RemoveChoiceOption(Nodes, Edges, node, option)) return;
        var afterEdges = Edges.ToList();
        PushGraphEdit(new DelegateEdit("Delete choice option",
            () => { node.Options.Insert(index, option); ReplaceEdges(edges); RefreshGraphNode(node); },
            () => { node.Options.Remove(option); ReplaceEdges(afterEdges); RefreshGraphNode(node); }));
    }

    public void MoveChoiceOptionTo(BranchOptionEditorItemViewModel? option, int newIndex)
    {
        MoveChoiceOptionTo(SelectedNode, option, newIndex);
    }

    public void MoveChoiceOptionTo(GraphNode? node, BranchOptionEditorItemViewModel? option, int newIndex)
    {
        if (node?.NodeKind != GraphNodeKind.ChoiceBranch || option is null) return;
        var oldIndex = node.Options.IndexOf(option); if (oldIndex < 0) return;
        var oldOutlets = Edges.ToDictionary(edge => edge, edge => edge.Outlet);
        if (!_graphEditingService.MoveChoiceOption(Nodes, Edges, node, option, newIndex)) return;
        var newOutlets = Edges.ToDictionary(edge => edge, edge => edge.Outlet);
        PushGraphEdit(CreateBranchMoveEdit("Move choice option", node, node.Options, oldIndex, newIndex, oldOutlets, newOutlets));
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
        if (!_graphEditingService.AddCondition(Nodes, Edges, node)) return;
        var condition = node.Conditions[^1];
        _propertyValues[(condition, nameof(condition.Expression))] = condition.Expression;
        PushGraphEdit(new DelegateEdit("Add condition",
            () => { node.Conditions.Remove(condition); RefreshGraphNode(node); },
            () => { node.Conditions.Add(condition); RefreshGraphNode(node); }));
    }

    [RelayCommand]
    private void RemoveCondition(BranchConditionEditorItemViewModel? condition)
    {
        RemoveConditionFrom(SelectedNode, condition);
    }

    public void RemoveConditionFrom(GraphNode? node, BranchConditionEditorItemViewModel? condition)
    {
        if (node?.NodeKind != GraphNodeKind.ConditionBranch || condition is null) return;
        var index = node.Conditions.IndexOf(condition); if (index < 0) return;
        var edges = Edges.ToList();
        if (!_graphEditingService.RemoveCondition(Nodes, Edges, node, condition)) return;
        var afterEdges = Edges.ToList();
        PushGraphEdit(new DelegateEdit("Delete condition",
            () => { node.Conditions.Insert(index, condition); ReplaceEdges(edges); RefreshGraphNode(node); },
            () => { node.Conditions.Remove(condition); ReplaceEdges(afterEdges); RefreshGraphNode(node); }));
    }

    public void MoveConditionTo(BranchConditionEditorItemViewModel? condition, int newIndex)
    {
        MoveConditionTo(SelectedNode, condition, newIndex);
    }

    public void MoveConditionTo(GraphNode? node, BranchConditionEditorItemViewModel? condition, int newIndex)
    {
        if (node?.NodeKind != GraphNodeKind.ConditionBranch || condition is null) return;
        var oldIndex = node.Conditions.IndexOf(condition); if (oldIndex < 0) return;
        var oldOutlets = Edges.ToDictionary(edge => edge, edge => edge.Outlet);
        if (!_graphEditingService.MoveCondition(Nodes, Edges, node, condition, newIndex)) return;
        var newOutlets = Edges.ToDictionary(edge => edge, edge => edge.Outlet);
        PushGraphEdit(CreateBranchMoveEdit("Move condition", node, node.Conditions, oldIndex, newIndex, oldOutlets, newOutlets));
    }

    [RelayCommand]
    private void ReorderCondition(ReorderRequest? request)
    {
        if (request?.Item is BranchConditionEditorItemViewModel condition)
            MoveConditionTo(condition, request.NewIndex);
    }

    public void SaveGraphDocument()
    {
        var edits = new List<IUndoableEdit>();
        foreach (var node in Nodes)
        {
            if (!_savedPositions.TryGetValue(node.Id, out var before) || (before.X == node.X && before.Y == node.Y)) continue;
            var after = (node.X, node.Y);
            edits.Add(new DelegateEdit("Move node", () => { node.X = before.X; node.Y = before.Y; }, () => { node.X = after.X; node.Y = after.Y; }));
            _savedPositions[node.Id] = after;
        }
        if (edits.Count > 0) PushGraphEdit(new CompositeEdit("Move nodes", edits));
    }

    public void PersistGraphDocument()
    {
        // The live ViewModels are the canonical GUI state. SaveCoreAsync maps them to files.
    }

    public Task SaveAsync() => _saveScheduler.SaveNowAsync(SaveCoreAsync);

    private async Task SaveCoreAsync()
    {
        if (_projectService.Current is not { } project)
            return;
        _isSaving = true;
        try
        {
            var document = _graphDocumentMapper.CreateDocument(project.Name, _documentService.CurrentDocument.Version, Nodes, Edges,
                _documentService.CurrentDocument.PlayerVariables, _documentService.CurrentDocument.SaveVariables);
            _saveCoordinator.SaveProjectDocument(project.RootPath, document, _graphDocumentMapper.CreateGroupEntriesSnapshot(Nodes));
            await _projectService.SaveAsync();
            _documentService.MarkSaved();
            _histories.MarkSaved();
            project.IsDirty = false;
        }
        finally { _isSaving = false; }
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
            _savedPositions.Clear();
            _propertyValues.Clear();
            var graph = _graphDocumentMapper.Load(loaded);
            foreach (var node in graph.Nodes)
            {
                node.IsRoot = node.NodeKind == GraphNodeKind.Entry;
                TrackNode(node);
                Nodes.Add(node);
            }
            foreach (var edge in graph.Edges)
                Edges.Add(edge);

            UpdateConnectorStates();
            _histories.Clear();
            CaptureVariableSnapshots();
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
            project.IsDirty = isDirty || _histories.IsDirty;
    }

    private void ExecuteGraphEdit(IUndoableEdit edit)
    {
        _isApplyingHistory = true;
        try { _histories.Graph.Execute(edit); }
        finally { _isApplyingHistory = false; }
        MarkGraphDirty();
    }

    private void PushGraphEdit(IUndoableEdit edit)
    {
        _histories.Graph.PushAlreadyApplied(edit);
        MarkGraphDirty();
    }

    private void OnHistoryChanged()
    {
        if (_projectService.Current is { } project)
            project.IsDirty = _histories.IsDirty;
        if (_histories.IsDirty && !_isLoadingGraph && !_isSaving)
            ScheduleAutoSave();
    }

    private void OnTrackedNodeChanged(GraphNode node, string propertyName)
    {
        RecordPropertyEdit(node, propertyName, node.Name, value => node.Name = (string)value!);
    }

    private void OnTrackedItemChanged(GraphNode node, object item, string propertyName)
    {
        var current = (item, propertyName) switch
        {
            (EntryEditorItemViewModel entry, nameof(EntryEditorItemViewModel.Type)) => entry.Type,
            (EntryEditorItemViewModel entry, nameof(EntryEditorItemViewModel.Condition)) => entry.Condition,
            (EntryEditorItemViewModel entry, nameof(EntryEditorItemViewModel.Parameters)) => entry.Parameters,
            (BranchOptionEditorItemViewModel option, nameof(BranchOptionEditorItemViewModel.Text)) => option.Text,
            (BranchOptionEditorItemViewModel option, nameof(BranchOptionEditorItemViewModel.Condition)) => option.Condition,
            (BranchConditionEditorItemViewModel condition, nameof(BranchConditionEditorItemViewModel.Expression)) => condition.Expression,
            _ => null
        };
        if (current is not null) RecordPropertyEdit(item, propertyName, current, value => SetTrackedProperty(item, propertyName, (string)value!));
    }

    private void RecordPropertyEdit(object item, string propertyName, object? current, Action<object?> setter)
    {
        var key = (item, propertyName);
        if (_isApplyingHistory || !_propertyValues.TryGetValue(key, out var before)) { _propertyValues[key] = current; return; }
        if (Equals(before, current)) return;
        _propertyValues[key] = current;
        PushGraphEdit(new DelegateEdit($"Edit {propertyName}",
            () => { _isApplyingHistory = true; try { setter(before); _propertyValues[key] = before; } finally { _isApplyingHistory = false; } },
            () => { _isApplyingHistory = true; try { setter(current); _propertyValues[key] = current; } finally { _isApplyingHistory = false; } }));
    }

    private static void SetTrackedProperty(object item, string propertyName, string value)
    {
        switch (item, propertyName)
        {
            case (EntryEditorItemViewModel entry, nameof(EntryEditorItemViewModel.Type)): entry.Type = value; break;
            case (EntryEditorItemViewModel entry, nameof(EntryEditorItemViewModel.Condition)): entry.Condition = value; break;
            case (EntryEditorItemViewModel entry, nameof(EntryEditorItemViewModel.Parameters)): entry.Parameters = value; break;
            case (BranchOptionEditorItemViewModel option, nameof(BranchOptionEditorItemViewModel.Text)): option.Text = value; break;
            case (BranchOptionEditorItemViewModel option, nameof(BranchOptionEditorItemViewModel.Condition)): option.Condition = value; break;
            case (BranchConditionEditorItemViewModel condition, nameof(BranchConditionEditorItemViewModel.Expression)): condition.Expression = value; break;
        }
    }

    public bool AddEntryTo(GraphNode groupNode)
    {
        if (groupNode.NodeKind != GraphNodeKind.LinearGroup) return false;
        if (!_graphEditingService.AddEntry(groupNode)) return false;
        var entry = groupNode.Entries[^1];
        CacheEntry(entry);
        PushGraphEdit(new DelegateEdit("Add entry", () => groupNode.Entries.Remove(entry), () => groupNode.Entries.Add(entry)));
        return true;
    }

    public bool RemoveEntryFrom(GraphNode groupNode, EntryEditorItemViewModel entry)
    {
        var index = groupNode.Entries.IndexOf(entry); if (index < 0 || !_graphEditingService.RemoveEntry(groupNode, entry)) return false;
        PushGraphEdit(new DelegateEdit("Delete entry", () => groupNode.Entries.Insert(index, entry), () => groupNode.Entries.Remove(entry)));
        return true;
    }

    public bool MoveEntryTo(GraphNode groupNode, EntryEditorItemViewModel entry, int newIndex)
    {
        var oldIndex = groupNode.Entries.IndexOf(entry); if (!_graphEditingService.MoveEntry(groupNode, entry, newIndex)) return false;
        PushGraphEdit(new CollectionMoveEdit<EntryEditorItemViewModel>("Move entry", groupNode.Entries, oldIndex, newIndex));
        return true;
    }

    private void TrackNode(GraphNode node)
    {
        _graphChangeTracker.Track(node);
        _savedPositions[node.Id] = (node.X, node.Y);
        _propertyValues[(node, nameof(GraphNode.Name))] = node.Name;
        foreach (var entry in node.Entries) CacheEntry(entry);
        foreach (var option in node.Options) { _propertyValues[(option, nameof(option.Text))] = option.Text; _propertyValues[(option, nameof(option.Condition))] = option.Condition; }
        foreach (var condition in node.Conditions) _propertyValues[(condition, nameof(condition.Expression))] = condition.Expression;
    }

    private void CacheEntry(EntryEditorItemViewModel entry)
    {
        _propertyValues[(entry, nameof(entry.Type))] = entry.Type;
        _propertyValues[(entry, nameof(entry.Condition))] = entry.Condition;
        _propertyValues[(entry, nameof(entry.Parameters))] = entry.Parameters;
    }

    private void CaptureVariableSnapshots()
    {
        _variableSnapshots[GalNet.Core.Variable.VariableScope.Player] = _documentService.CurrentDocument.PlayerVariables.Select(item => item.Clone()).ToList();
        _variableSnapshots[GalNet.Core.Variable.VariableScope.Save] = _documentService.CurrentDocument.SaveVariables.Select(item => item.Clone()).ToList();
    }

    private void RecordVariableDefinitionsChange(GalNet.Core.Variable.VariableScope scope)
    {
        if (_isLoadingGraph || _isApplyingHistory) return;
        var current = GetVariableDefinitions(scope).Select(item => item.Clone()).ToList();
        if (!_variableSnapshots.TryGetValue(scope, out var before)) { _variableSnapshots[scope] = current; return; }
        _variableSnapshots[scope] = current.Select(item => item.Clone()).ToList();
        PushGraphEdit(new DelegateEdit("Edit variable definitions",
            () => ReplaceVariableDefinitions(scope, before),
            () => ReplaceVariableDefinitions(scope, current)));
    }

    private List<GalNet.Core.Variable.ProjectVariableDefinition> GetVariableDefinitions(GalNet.Core.Variable.VariableScope scope) =>
        scope == GalNet.Core.Variable.VariableScope.Player ? _documentService.CurrentDocument.PlayerVariables : _documentService.CurrentDocument.SaveVariables;

    private void ReplaceVariableDefinitions(GalNet.Core.Variable.VariableScope scope, IReadOnlyList<GalNet.Core.Variable.ProjectVariableDefinition> values)
    {
        _isApplyingHistory = true;
        try
        {
            var target = GetVariableDefinitions(scope);
            target.Clear(); target.AddRange(values.Select(item => item.Clone()));
            _variableSnapshots[scope] = target.Select(item => item.Clone()).ToList();
            OnPropertyChanged(nameof(AllProjectVariableDefinitions));
            VariableDefinitionsChanged?.Invoke();
        }
        finally { _isApplyingHistory = false; }
    }

    private void ReplaceEdges(IReadOnlyList<GraphEdge> edges)
    {
        Edges.Clear(); foreach (var edge in edges) Edges.Add(edge); UpdateConnectorStates();
    }

    private void RefreshGraphNode(GraphNode node) { node.RefreshConnectors(); UpdateConnectorStates(); }

    private IUndoableEdit CreateBranchMoveEdit<T>(string description, GraphNode node, ObservableCollection<T> items,
        int before, int after, IReadOnlyDictionary<GraphEdge, int> oldOutlets, IReadOnlyDictionary<GraphEdge, int> newOutlets)
    {
        void Apply(int from, int to, IReadOnlyDictionary<GraphEdge, int> outlets)
        {
            items.Move(from, to); foreach (var pair in outlets) pair.Key.Outlet = pair.Value; RefreshGraphNode(node);
        }
        return new DelegateEdit(description, () => Apply(after, before, oldOutlets), () => Apply(before, after, newOutlets));
    }

    public GraphNode? EntryNode => Nodes.FirstOrDefault(n => n.NodeKind == GraphNodeKind.Entry);
}

