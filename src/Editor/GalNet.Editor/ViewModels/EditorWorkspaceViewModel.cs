using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    public bool ExpandNewEntries => _editorSettings.GetSettings().ExpandNewEntries;
    private readonly IEditorLocalizationService _localization;
    private readonly IVariableDefinitionService _variableDefinitionService;
    private readonly Action<GalProject?> _projectChangedHandler;
    private readonly Action<GalNet.Core.Variable.VariableScope> _definitionsChangedHandler;
    private readonly IProjectSaveScheduler _saveScheduler;
    private readonly GroupEntryAuthoringService _entryAuthoring;
    private readonly GraphAuthoringService _graphAuthoring;
    private EditorText _statusMessage = new("Editor.Status.Ready");
    private void SetStatus(EditorText text) { _statusMessage = text; StatusText = text.Render(_localization); }
    private void OnCultureChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == "Item[]") StatusText = _statusMessage.Render(_localization);
    }
    private bool _disposed;
    private bool _isSaving;
    public IUndoRedoHistory UndoRedoHistory => _histories.Content;
    public event Action? VariableDefinitionsChanged;

    [ObservableProperty]
    private GraphNode? _selectedNode;

    [ObservableProperty]
    private GraphEdge? _selectedEdge;

    [ObservableProperty]
    private AssetEntry? _selectedAsset;

    [ObservableProperty]
    private string _statusText = "Ready";

    private readonly ObservableCollection<GraphNode> _nodes = [];
    public ReadOnlyObservableCollection<GraphNode> Nodes { get; }
    public ExternalDebugPanelViewModel? ExternalDebug => _projectService.Current?.Services.GetService<ExternalDebugPanelViewModel>();
    private readonly ObservableCollection<GraphEdge> _edges = [];
    public ReadOnlyObservableCollection<GraphEdge> Edges { get; }
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
        EditorWorkspacePersistence persistence, GraphAuthoringService graphAuthoring, GroupEntryAuthoringService entryAuthoring)
    {
        Nodes = new(_nodes);
        Edges = new(_edges);
        _projectService = projectService;
        _histories = histories;
        _dockFactory = dockFactory;
        _documentService = documentService;
        _graphEditingService = graphEditingService;
        _editorSettings = editorSettings;
        _localization = localization;
        _localization.PropertyChanged += OnCultureChanged;
        _saveScheduler = saveScheduler;
        _selection = selection;
        _persistence = persistence;
        _graphAuthoring = graphAuthoring;
        _entryAuthoring = entryAuthoring;
        _variableDefinitionService = variableDefinitionService;
        _projectChangedHandler = _ => LoadCurrentProjectGraph();
        _definitionsChangedHandler = _ =>
        {
            OnPropertyChanged(nameof(AllProjectVariableDefinitions));
            VariableDefinitionsChanged?.Invoke();
        };
        _projectService.CurrentChanged += _projectChangedHandler;
        _variableDefinitionService.DefinitionsChanged += _definitionsChangedHandler;
        _histories.Changed += OnHistoryChanged;
        SetStatus(_statusMessage);
        LoadCurrentProjectGraph();
        _documentService.DocumentChanged += SynchronizeGraph;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _localization.PropertyChanged -= OnCultureChanged;
        _projectService.CurrentChanged -= _projectChangedHandler;
        _variableDefinitionService.DefinitionsChanged -= _definitionsChangedHandler;
        _histories.Changed -= OnHistoryChanged;
        _documentService.DocumentChanged -= SynchronizeGraph;
        foreach (var node in Nodes) { node.RenameRequested = null; node.BranchValueRequested = null; }
        foreach (var edge in Edges) edge.Dispose();
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
        var id = await _graphAuthoring.AddNodeAsync(kind, x, y, from?.Node.Id, from?.Index ?? 0, from?.ItemId);
        var node = Nodes.Single(node => node.Id == id);
        SelectNode(node);
        return node;
    }

    public Task DeleteNodeAsync(GraphNode node) => !node.CanDelete || !Nodes.Contains(node)
        ? Task.CompletedTask : _graphAuthoring.DeleteNodesAsync([node.Id]);

    public void ReportGraphError(Exception failure)
    {
        SetStatus(EditorText.Error(failure));
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

    public void AddChoiceOptionTo(GraphNode? node) { if (node?.NodeKind == GraphNodeKind.ChoiceBranch) EditBranch(() => _graphAuthoring.AddBranchItem(node.Id)); }

    [RelayCommand]
    private void RemoveChoiceOption(BranchOptionEditorItemViewModel? option)
    {
        RemoveChoiceOptionFrom(SelectedNode, option);
    }

    public void RemoveChoiceOptionFrom(GraphNode? node, BranchOptionEditorItemViewModel? option) { if (node is not null && option is not null) EditBranch(() => _graphAuthoring.RemoveBranchItem(node.Id, option.Id)); }

    public void MoveChoiceOptionTo(BranchOptionEditorItemViewModel? option, int newIndex)
    {
        MoveChoiceOptionTo(SelectedNode, option, newIndex);
    }

    public void MoveChoiceOptionTo(GraphNode? node, BranchOptionEditorItemViewModel? option, int newIndex) { if (node is not null && option is not null) EditBranch(() => _graphAuthoring.MoveBranchItem(node.Id, option.Id, newIndex)); }

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

    public void AddConditionTo(GraphNode? node) { if (node?.NodeKind == GraphNodeKind.ConditionBranch) EditBranch(() => _graphAuthoring.AddBranchItem(node.Id)); }

    [RelayCommand]
    private void RemoveCondition(BranchConditionEditorItemViewModel? condition)
    {
        RemoveConditionFrom(SelectedNode, condition);
    }

    public void RemoveConditionFrom(GraphNode? node, BranchConditionEditorItemViewModel? condition) { if (node is not null && condition is not null) EditBranch(() => _graphAuthoring.RemoveBranchItem(node.Id, condition.Id)); }

    public void MoveConditionTo(BranchConditionEditorItemViewModel? condition, int newIndex)
    {
        MoveConditionTo(SelectedNode, condition, newIndex);
    }

    public void MoveConditionTo(GraphNode? node, BranchConditionEditorItemViewModel? condition, int newIndex) { if (node is not null && condition is not null) EditBranch(() => _graphAuthoring.MoveBranchItem(node.Id, condition.Id, newIndex)); }

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
        if (_projectService.Current is null)
            return;

        _isSaving = true;
        try { await _persistence.SaveCurrentProjectAsync(); }
        finally { _isSaving = false; }
    }

    private void UpdateConnectorStates() => _graphEditingService.UpdateConnectorStates(Nodes, Edges);

    private void LoadCurrentProjectGraph()
    {
        _isLoadingGraph = true;
        ClearSelection();
        foreach (var node in Nodes) { node.RenameRequested = null; node.BranchValueRequested = null; }
        foreach (var edge in Edges) edge.Dispose();
        _nodes.Clear();
        _edges.Clear();

        try
        {
            _isLoadingGraph = true;
            var graph = _persistence.LoadCurrentProject();
            if (graph is null)
            {
                return;
            }

            foreach (var node in graph.Nodes)
            {
                node.IsRoot = node.NodeKind == GraphNodeKind.Entry;
                TrackNode(node);
                _nodes.Add(node);
            }
            foreach (var edge in graph.Edges)
                _edges.Add(edge);

            UpdateConnectorStates();
            SelectNode(EntryNode ?? Nodes.FirstOrDefault());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load project graph");
            ClearSelection();
            _nodes.Clear();
            _edges.Clear();
            SetStatus(new("Status.GraphLoadFailed", EditorText.Error(ex)));
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
            foreach (var node in Nodes.ToArray())
                if (!source.Nodes.Any(dto => ReferenceEquals(dto, node.Source)))
                {
                    node.RenameRequested = null; node.BranchValueRequested = null;
                    _nodes.Remove(node);
                    if (node.NodeKind == GraphNodeKind.LinearGroup) _dockFactory.CloseGroupEditor(node.Id);
                }
            for (var index = 0; index < source.Nodes.Count; index++)
            {
                var dto = source.Nodes[index];
                var node = Nodes.FirstOrDefault(node => ReferenceEquals(node.Source, dto));
                if (node is null) { node = _persistence.ProjectNode(dto); _nodes.Insert(index, node); }
                else if (Nodes.IndexOf(node) != index) _nodes.Move(Nodes.IndexOf(node), index);
                node.RefreshAuthorValues();
                TrackNode(node);
            }
            foreach (var edge in Edges.ToArray())
                if (!source.Edges.Any(dto => ReferenceEquals(dto, edge.Source)) || !Nodes.Contains(edge.From) || !Nodes.Contains(edge.To))
                { _edges.Remove(edge); edge.Dispose(); }
            for (var index = 0; index < source.Edges.Count; index++)
            {
                var dto = source.Edges[index];
                var edge = Edges.FirstOrDefault(edge => ReferenceEquals(edge.Source, dto));
                if (edge is null)
                {
                    edge = new GraphEdge(Nodes.Single(node => node.Id == dto.FromNodeId), Nodes.Single(node => node.Id == dto.ToNodeId), dto);
                    _edges.Insert(index, edge);
                }
                else if (Edges.IndexOf(edge) != index) _edges.Move(Edges.IndexOf(edge), index);
                edge.Outlet = dto.FromOutlet;
            }
            UpdateConnectorStates();
            _selection.SelectNodes(Nodes.Where(node => selected.Contains(node.Id)));
            if (selectedEdge is not null) _selection.SelectEdge(Edges.FirstOrDefault(edge => edge.Id == selectedEdge));
            SyncSelectionProperties();
        }
        finally { _isLoadingGraph = false; }
    }

    private void ScheduleAutoSave()
    {
        if (!_editorSettings.GetSettings().AutoSaveProject)
            return;

        _saveScheduler.Schedule(async () =>
        {
            await SaveCoreAsync();
            Avalonia.Threading.Dispatcher.UIThread.Post(() => SetStatus(new("Status.AutoSaved")));
        });
    }

    private void OnHistoryChanged()
    {
        if (_disposed) return;
        if (_histories.IsDirty && !_histories.Content.IsBusy && !_isLoadingGraph && !_isSaving)
            ScheduleAutoSave();
    }

    public IReadOnlyList<EntryEditorItemViewModel> InsertEntriesInto(GraphNode groupNode, int index, int count, string? type = null) =>
        groupNode.NodeKind == GraphNodeKind.LinearGroup
            ? ResolveEntries(groupNode, _entryAuthoring.Insert(groupNode.Id, index, count, type)) : [];

    private static IReadOnlyList<EntryEditorItemViewModel> ResolveEntries(GraphNode group, IReadOnlyList<string> ids) =>
        group.Entries.Where(entry => ids.Contains(entry.StableId)).ToArray();

    public bool RemoveEntryFrom(GraphNode groupNode, EntryEditorItemViewModel entry)
    {
        if (!groupNode.Entries.Contains(entry)) return false;
        _entryAuthoring.Delete(groupNode.Id, [entry.StableId]);
        return true;
    }

    public void ChangeEntryType(GraphNode groupNode, EntryEditorItemViewModel entry, string type, IReadOnlyDictionary<string, System.Text.Json.JsonElement> parameters)
    {
        if (!groupNode.Entries.Contains(entry) || entry.Type == type) return;
        entry.ChangeAuthorType(type, parameters);
    }

    private void TrackNode(GraphNode node)
    {
        node.GameTexts = _projectService.Current?.Services.GetService<GameTextAuthoringService>();
        foreach (var option in node.Options) option.BindText(node.GameTexts, node.Id);
        node.RenameRequested = value => { try { _graphAuthoring.Rename(node.Id, value); } catch (Exception failure) { ReportGraphError(failure); } };
        node.BranchValueRequested = (itemId, field, value) => EditBranch(() => _graphAuthoring.SetBranchValue(node.Id, itemId, field, value));
    }

    private void EditBranch(Action edit) { try { edit(); } catch (Exception failure) { ReportGraphError(failure); } }
    public string CopySelection() => _graphAuthoring.CopyNodes(SelectedNodes.Select(node => node.Id));
    public async Task PasteAsync(string text, double x, double y) { var ids = await _graphAuthoring.PasteAsync(text, x, y); SelectNodes(Nodes.Where(node => ids.Contains(node.Id))); SetStatus(new("Status.GraphPasted", ids.Count)); }
    public GraphNode? EntryNode => Nodes.FirstOrDefault(n => n.NodeKind == GraphNodeKind.Entry);
}
