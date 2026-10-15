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
using GalNet.Editor.History;
using GalNet.Editor.Models;
using GalNet.Editor.Services;
using GalNet.Editor.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace GalNet.Editor.ViewModels;

public partial class EditorWorkspaceViewModel : ObservableObject, IDisposable, IUndoRedoTarget
{
    private readonly IProjectService _projectService;
    private readonly EditorHistories _histories;
    private readonly EditorDockFactory _dockFactory;
    private readonly IGraphEditingService _graphEditingService;
    private readonly GraphSelectionState _selection;
    private readonly EditorWorkspacePersistence _persistence;
    private readonly IEditorSettingsService _editorSettings;
    private readonly IEditorLocalizationService _localization;
    private readonly IVariableDefinitionService _variableDefinitionService;
    private readonly Action<GalProject?> _projectChangedHandler;
    private readonly Action<GalNet.Core.Variable.VariableScope> _definitionsChangedHandler;
    private readonly IProjectSaveScheduler _saveScheduler;
    private readonly GraphChangeTracker _graphChangeTracker;
    private readonly GraphAuthoringService _graphAuthoring;
    private bool _disposed;
    private bool _isSaving;
    private bool _isApplyingHistory;
    private readonly Dictionary<(object Item, string Property), object?> _propertyValues = [];
    private readonly Dictionary<GalNet.Core.Variable.VariableScope, List<GalNet.Core.Variable.ProjectVariableDefinition>> _variableSnapshots = [];
    public IUndoRedoHistory UndoRedoHistory => _histories.Content;
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
    public ObservableCollection<GraphNode> SelectedNodes => _selection.SelectedNodes;
    public GraphViewportState GraphViewport => _projectService.Current?.EditorState.GraphViewport ?? _fallbackViewport;
    public bool HasMultipleNodeSelection => _selection.HasMultipleNodes;

    private readonly GraphViewportState _fallbackViewport = new();
    private bool _isLoadingGraph;

    public EditorWorkspaceViewModel(
        IProjectService projectService,
        EditorHistories histories,
        EditorDockFactory dockFactory,
        IEditorDocumentService documentService,
        IVariableDefinitionService variableDefinitionService,
        IGraphEditingService graphEditingService,
        IEditorSettingsService editorSettings,
        IEditorLocalizationService localization,
        IProjectSaveScheduler saveScheduler,
        GraphSelectionState selection,
        EditorWorkspacePersistence persistence, GraphAuthoringService graphAuthoring)
    {
        _projectService = projectService;
        _histories = histories;
        _dockFactory = dockFactory;
        _documentService = documentService;
        _graphEditingService = graphEditingService;
        _editorSettings = editorSettings;
        _localization = localization;
        _saveScheduler = saveScheduler;
        _selection = selection;
        _persistence = persistence;
        _graphAuthoring = graphAuthoring;
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
            () => _isLoadingGraph || _histories.Content.IsBusy,
            OnTrackedItemChanged);
        Nodes.CollectionChanged += OnGraphCollectionChanged;
        Edges.CollectionChanged += OnGraphCollectionChanged;
        LoadCurrentProjectGraph();
        _documentService.DocumentChanged += SynchronizeGraph;
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
        _documentService.DocumentChanged -= SynchronizeGraph;
        foreach (var node in Nodes) node.RenameRequested = null;
        foreach (var edge in Edges) edge.Dispose();
        _graphChangeTracker.Dispose();
        Nodes.CollectionChanged -= OnGraphCollectionChanged;
        Edges.CollectionChanged -= OnGraphCollectionChanged;
        GC.SuppressFinalize(this);
    }

    public void SelectNode(GraphNode? node, bool additive = false)
    {
        _selection.SelectNode(node, additive);
        SyncSelectionProperties();
    }

    public void SelectNodes(IEnumerable<GraphNode> nodes)
    {
        _selection.SelectNodes(nodes);
        SyncSelectionProperties();
    }

    public void SelectEdge(GraphEdge? edge)
    {
        _selection.SelectEdge(edge);
        SyncSelectionProperties();
    }

    public void ClearSelection()
    {
        _selection.Clear();
        SyncSelectionProperties();
    }

    private void SyncSelectionProperties()
    {
        SelectedNode = _selection.SelectedNode;
        SelectedEdge = _selection.SelectedEdge;
        OnPropertyChanged(nameof(HasMultipleNodeSelection));
    }

    public async Task DeleteSelectionAsync()
    {
        if (SelectedEdge is not null) { DeleteEdge(SelectedEdge); return; }
        await _graphAuthoring.DeleteNodesAsync(SelectedNodes.Where(node => node.CanDelete).Select(node => node.Id).ToArray());
    }

    public void DeleteEdge(GraphEdge edge) => _graphAuthoring.DeleteEdge(edge.Id);

    public void SaveGraphViewport()
    {
        if (_projectService.Current is not { } project)
            return;

        var content = project.Services.GetRequiredService<IProjectContentSession>();
        _saveScheduler.Schedule(content.SaveViewStateAsync);
    }

    public async Task<GraphNode> AddNodeAsync(GraphNodeKind kind, double x, double y, GraphConnector? from = null)
    {
        var id = await _graphAuthoring.AddNodeAsync(kind, x, y, from?.Node.Id, from?.Index ?? 0);
        var node = Nodes.Single(node => node.Id == id);
        SelectNode(node);
        return node;
    }

    public Task DeleteNodeAsync(GraphNode node) => !node.CanDelete || !Nodes.Contains(node)
        ? Task.CompletedTask : _graphAuthoring.DeleteNodesAsync([node.Id]);

    public void ReportGraphError(Exception failure)
    {
        StatusText = failure.Message;
        _projectService.Current?.Services.GetService<IProjectContentSession>()?.ReportError(failure);
        Log.Error(failure, "Graph edit failed");
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
        if (first.Kind == second.Kind || output.Node.Id == input.Node.Id) return;
        _graphAuthoring.Connect(output.Node.Id, output.Index, input.Node.Id);
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

    public void CommitNodePositions() => _graphAuthoring.Move(Nodes.ToDictionary(node => node.Id, node => (node.X, node.Y)));
    public void CancelNodeDrag()
    {
        foreach (var node in Nodes) node.RefreshAuthorValues();
    }

    public string Localize(string key) => _localization[key];

    public Task SaveAsync() => _saveScheduler.SaveNowAsync(SaveCoreAsync);

    private async Task SaveCoreAsync()
    {
        if (_projectService.Current is not { } project)
            return;

        _isSaving = true;
        try
        {
            if (!await _persistence.SaveCurrentProjectAsync())
                return;
            _histories.MarkSaved();
            if (_histories.IsDirty) _documentService.MarkDirty();
            else _documentService.MarkSaved();
            project.IsDirty = _histories.IsDirty;
        }
        finally { _isSaving = false; }
    }

    public async Task CreateProjectAsync(string name, string path, string contractsDirectory)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Project name is required.");

        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Project path is required.");

        var projectPath = Path.Combine(path, name);
        await _projectService.CreateAsync(projectPath, name, new ProjectSettings(), contractsDirectory);
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
        _isLoadingGraph = true;
        ClearSelection();
        _graphChangeTracker.Clear();
        foreach (var node in Nodes) node.RenameRequested = null;
        foreach (var edge in Edges) edge.Dispose();
        Nodes.Clear();
        Edges.Clear();

        try
        {
            _isLoadingGraph = true;
            var graph = _persistence.LoadCurrentProject();
            if (graph is null)
            {
                return;
            }

            _propertyValues.Clear();
            foreach (var node in graph.Nodes)
            {
                node.IsRoot = node.NodeKind == GraphNodeKind.Entry;
                TrackNode(node);
                Nodes.Add(node);
            }
            foreach (var edge in graph.Edges)
                Edges.Add(edge);

            UpdateConnectorStates();
            CaptureVariableSnapshots();
            SelectNode(EntryNode ?? Nodes.FirstOrDefault());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load project graph");
            ClearSelection();
            Nodes.Clear();
            Edges.Clear();
            StatusText = $"Failed to load graph: {ex.Message}";
            throw;
        }
        finally
        {
            _isLoadingGraph = false;
        }
    }

    private void SynchronizeGraph()
    {
        if (_disposed || _isLoadingGraph) return;
        _isLoadingGraph = true;
        try
        {
            var selected = SelectedNodes.Select(node => node.Id).ToHashSet();
            var selectedEdge = SelectedEdge?.Id;
            var source = _documentService.CurrentDocument;
            _graphChangeTracker.Clear();
            foreach (var node in Nodes.ToArray())
                if (!source.Nodes.Any(dto => ReferenceEquals(dto, node.Source)))
                {
                    node.RenameRequested = null;
                    Nodes.Remove(node);
                    if (node.NodeKind == GraphNodeKind.LinearGroup) _dockFactory.CloseGroupEditor(node.Id);
                }
            for (var index = 0; index < source.Nodes.Count; index++)
            {
                var dto = source.Nodes[index];
                var node = Nodes.FirstOrDefault(node => ReferenceEquals(node.Source, dto));
                if (node is null) { node = _persistence.ProjectNode(dto); Nodes.Insert(index, node); }
                else if (Nodes.IndexOf(node) != index) Nodes.Move(Nodes.IndexOf(node), index);
                node.RefreshAuthorValues();
                TrackNode(node);
            }
            foreach (var edge in Edges.ToArray())
                if (!source.Edges.Any(dto => ReferenceEquals(dto, edge.Source)) || !Nodes.Contains(edge.From) || !Nodes.Contains(edge.To))
                { Edges.Remove(edge); edge.Dispose(); }
            for (var index = 0; index < source.Edges.Count; index++)
            {
                var dto = source.Edges[index];
                var edge = Edges.FirstOrDefault(edge => ReferenceEquals(edge.Source, dto));
                if (edge is null)
                {
                    edge = new GraphEdge(Nodes.Single(node => node.Id == dto.FromNodeId), Nodes.Single(node => node.Id == dto.ToNodeId), dto);
                    Edges.Insert(index, edge);
                }
                else if (Edges.IndexOf(edge) != index) Edges.Move(Edges.IndexOf(edge), index);
            }
            UpdateConnectorStates();
            _selection.SelectNodes(Nodes.Where(node => selected.Contains(node.Id)));
            if (selectedEdge is not null) _selection.SelectEdge(Edges.FirstOrDefault(edge => edge.Id == selectedEdge));
            SyncSelectionProperties();
        }
        finally { _isLoadingGraph = false; }
    }

    private void MarkGraphDirty()
    {
        _documentService.MarkDirty();
        if (_projectService.Current is { } project)
            project.IsDirty = true;
        ScheduleAutoSave();
    }

    private void OnGraphCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs args)
    {
        if (_isLoadingGraph) return;
        _documentService.CurrentDocument.Nodes = Nodes.Select(node => node.Source).ToList();
        _documentService.CurrentDocument.Edges = Edges.Select(edge => edge.Source).ToList();
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
        try { _histories.Content.Execute(edit); }
        finally { _isApplyingHistory = false; }
        MarkGraphDirty();
    }

    private void PushGraphEdit(IUndoableEdit edit)
    {
        _histories.Content.PushAlreadyApplied(edit);
        MarkGraphDirty();
    }

    private void OnHistoryChanged()
    {
        if (_disposed) return;
        _propertyValues.Clear();
        foreach (var node in Nodes)
        {
            foreach (var entry in node.Entries) CacheEntry(entry);
            foreach (var option in node.Options)
            {
                _propertyValues[(option, nameof(option.Text))] = option.Text;
                _propertyValues[(option, nameof(option.Condition))] = option.Condition;
            }
            foreach (var condition in node.Conditions) _propertyValues[(condition, nameof(condition.Expression))] = condition.Expression;
        }
        CaptureVariableSnapshots();
        if (_projectService.Current is { } project)
            project.IsDirty = _histories.IsDirty;
        if (_histories.IsDirty && !_histories.Content.IsBusy && !_isLoadingGraph && !_isSaving)
            ScheduleAutoSave();
    }

    private void OnTrackedItemChanged(GraphNode node, object item, string propertyName)
    {
        object? current = (item, propertyName) switch
        {
            (EntryEditorItemViewModel entry, nameof(EntryEditorItemViewModel.Type)) => entry.Type,
            (EntryEditorItemViewModel entry, nameof(EntryEditorItemViewModel.Condition)) => entry.Condition,
            (EntryEditorItemViewModel entry, nameof(EntryEditorItemViewModel.Parameters)) => entry.Parameters,
            (BranchOptionEditorItemViewModel option, nameof(BranchOptionEditorItemViewModel.Text)) => option.Text,
            (BranchOptionEditorItemViewModel option, nameof(BranchOptionEditorItemViewModel.Condition)) => option.Condition,
            (BranchConditionEditorItemViewModel condition, nameof(BranchConditionEditorItemViewModel.Expression)) => condition.Expression,
            _ => null
        };
        if (current is not null) RecordPropertyEdit(item, propertyName, current, value => SetTrackedProperty(item, propertyName, value));
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

    private static void SetTrackedProperty(object item, string propertyName, object? value)
    {
        switch (item, propertyName)
        {
            case (EntryEditorItemViewModel entry, nameof(EntryEditorItemViewModel.Type)): entry.Type = (string)value!; break;
            case (EntryEditorItemViewModel entry, nameof(EntryEditorItemViewModel.Condition)): entry.Condition = (string)value!; break;
            case (EntryEditorItemViewModel entry, nameof(EntryEditorItemViewModel.Parameters)): entry.Parameters = new Dictionary<string, string>((Dictionary<string, string>)value!); break;
            case (BranchOptionEditorItemViewModel option, nameof(BranchOptionEditorItemViewModel.Text)): option.Text = (string)value!; break;
            case (BranchOptionEditorItemViewModel option, nameof(BranchOptionEditorItemViewModel.Condition)): option.Condition = (string)value!; break;
            case (BranchConditionEditorItemViewModel condition, nameof(BranchConditionEditorItemViewModel.Expression)): condition.Expression = (string)value!; break;
        }
    }

    public IReadOnlyList<EntryEditorItemViewModel> InsertEntriesInto(GraphNode groupNode, int index, int count)
    {
        if (groupNode.NodeKind != GraphNodeKind.LinearGroup) return [];
        var inserted = _graphEditingService.InsertEntries(groupNode, index, Math.Clamp(count, 1, 1000));
        if (inserted.Count == 0) return inserted;
        foreach (var entry in inserted) CacheEntry(entry);
        var actualIndex = groupNode.Entries.IndexOf(inserted[0]);
        PushGraphEdit(new DelegateEdit(
            inserted.Count == 1 ? "Add entry" : $"Add {inserted.Count} entries",
            () =>
            {
                foreach (var entry in inserted) groupNode.Entries.Remove(entry);
                RenumberGroupEntries(groupNode);
            },
            () =>
            {
                for (var offset = 0; offset < inserted.Count; offset++)
                    groupNode.Entries.Insert(Math.Min(actualIndex + offset, groupNode.Entries.Count), inserted[offset]);
                RenumberGroupEntries(groupNode);
            }));
        return inserted;
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

    private static void RenumberGroupEntries(GraphNode groupNode)
    {
        for (var index = 0; index < groupNode.Entries.Count; index++)
            groupNode.Entries[index].Id = index + 1;
    }

    private void TrackNode(GraphNode node)
    {
        node.RenameRequested = value => { try { _graphAuthoring.Rename(node.Id, value); } catch (Exception failure) { ReportGraphError(failure); } };
        _graphChangeTracker.Track(node);
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

    private DelegateEdit CreateBranchMoveEdit<T>(string description, GraphNode node, ObservableCollection<T> items,
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
