using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.Settings;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Project;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Abstraction.Sessions;
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
    private readonly IEditorSession _session;
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
    private bool _ignoreSessionDocumentChanged;
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
        IEditorSession session,
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
        _session = session;
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
            SynchronizeVariableDefinitionsToSession();
            OnPropertyChanged(nameof(AllProjectVariableDefinitions));
            VariableDefinitionsChanged?.Invoke();
        };
        _projectService.CurrentChanged += _projectChangedHandler;
        _documentService.DirtyStateChanged += OnDocumentDirtyStateChanged;
        _variableDefinitionService.DefinitionsChanged += _definitionsChangedHandler;
        _session.DocumentChanged += OnSessionDocumentChanged;
        _session.HistoryChanged += OnSessionHistoryChanged;
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
        _session.DocumentChanged -= OnSessionDocumentChanged;
        _session.HistoryChanged -= OnSessionHistoryChanged;
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
        ExecuteSessionCommand(new DeleteEdgeCommand(EdgeId: edge.Id));
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
        var result = ExecuteSessionCommand(new CreateNodeCommand(
            id,
            kind switch
            {
                GraphNodeKind.Entry => EditorNodeKind.Entry,
                GraphNodeKind.LinearGroup => EditorNodeKind.LinearGroup,
                GraphNodeKind.ChoiceBranch => EditorNodeKind.ChoiceBranch,
                _ => EditorNodeKind.ConditionBranch
            },
            name,
            x,
            y));
        var node = Nodes.FirstOrDefault(item => item.Id == id)
            ?? throw new InvalidOperationException(result.Diagnostics.FirstOrDefault()?.Message ?? "Node creation failed.");
        SelectNode(node);
        return node;
    }

    public void DeleteNode(GraphNode node)
    {
        var result = ExecuteSessionCommand(new DeleteNodeCommand(node.Id));
        if (result.Success && node.NodeKind == GraphNodeKind.LinearGroup)
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
        ExecuteSessionCommand(new ConnectNodesCommand(output.Node.Id, output.Index, input.Node.Id));
    }

    [RelayCommand]
    private void AddChoiceOption()
    {
        AddChoiceOptionTo(SelectedNode);
    }

    public void AddChoiceOptionTo(GraphNode? node)
    {
        if (node?.NodeKind != GraphNodeKind.ChoiceBranch) return;
        ExecuteSessionCommand(new AddChoiceOptionCommand(
            node.Id,
            Guid.NewGuid().ToString("N"),
            Text: $"Option {node.Options.Count + 1}"));
    }

    [RelayCommand]
    private void RemoveChoiceOption(BranchOptionEditorItemViewModel? option)
    {
        RemoveChoiceOptionFrom(SelectedNode, option);
    }

    public void RemoveChoiceOptionFrom(GraphNode? node, BranchOptionEditorItemViewModel? option)
    {
        if (node?.NodeKind != GraphNodeKind.ChoiceBranch || option is null) return;
        ExecuteSessionCommand(new DeleteChoiceOptionCommand(node.Id, option.Id));
    }

    public void MoveChoiceOptionTo(BranchOptionEditorItemViewModel? option, int newIndex)
    {
        MoveChoiceOptionTo(SelectedNode, option, newIndex);
    }

    public void MoveChoiceOptionTo(GraphNode? node, BranchOptionEditorItemViewModel? option, int newIndex)
    {
        if (node?.NodeKind != GraphNodeKind.ChoiceBranch || option is null) return;
        ExecuteSessionCommand(new MoveChoiceOptionCommand(node.Id, option.Id, newIndex));
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
        ExecuteSessionCommand(new AddBranchConditionCommand(
            node.Id,
            Guid.NewGuid().ToString("N"),
            Expression: "true"));
    }

    [RelayCommand]
    private void RemoveCondition(BranchConditionEditorItemViewModel? condition)
    {
        RemoveConditionFrom(SelectedNode, condition);
    }

    public void RemoveConditionFrom(GraphNode? node, BranchConditionEditorItemViewModel? condition)
    {
        if (node?.NodeKind != GraphNodeKind.ConditionBranch || condition is null) return;
        ExecuteSessionCommand(new DeleteBranchConditionCommand(node.Id, condition.Id));
    }

    public void MoveConditionTo(BranchConditionEditorItemViewModel? condition, int newIndex)
    {
        MoveConditionTo(SelectedNode, condition, newIndex);
    }

    public void MoveConditionTo(GraphNode? node, BranchConditionEditorItemViewModel? condition, int newIndex)
    {
        if (node?.NodeKind != GraphNodeKind.ConditionBranch || condition is null) return;
        ExecuteSessionCommand(new MoveBranchConditionCommand(node.Id, condition.Id, newIndex));
    }

    [RelayCommand]
    private void ReorderCondition(ReorderRequest? request)
    {
        if (request?.Item is BranchConditionEditorItemViewModel condition)
            MoveConditionTo(condition, request.NewIndex);
    }

    public void SaveGraphDocument()
    {
        var positions = Nodes
            .Select(node => new { node, persisted = _session.Document.Graph.Nodes.FirstOrDefault(item => item.Id == node.Id) })
            .Where(pair => pair.persisted is not null && (pair.persisted.X != pair.node.X || pair.persisted.Y != pair.node.Y))
            .Select(pair => new NodePositionChange(pair.node.Id, pair.node.X, pair.node.Y))
            .ToList();
        if (positions.Count > 0)
            ExecuteLocalCommand(new MoveNodesCommand(positions));
    }

    public void PersistGraphDocument()
    {
        // The command session owns the canonical document. All persisted editor changes
        // have already been applied to it before this method is reached.
    }

    public Task SaveAsync() => _saveScheduler.SaveNowAsync(SaveCoreAsync);

    private async Task SaveCoreAsync()
    {
        if (_projectService.Current is null)
            return;

        CopySessionSettingsToCurrentProject();
        await _session.SaveAsync();
        await _projectService.SaveAsync();
        _documentService.MarkSaved();
        if (_projectService.Current is { } project)
            project.IsDirty = _session.IsDirty;
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
            var sessionSnapshot = GalNet.Editor.Abstraction.Changes.EditorDocumentCloner.Clone(_session.Document);
            var loaded = new LoadedEditorProjectDocument
            {
                Document = sessionSnapshot.Graph,
                GroupEntries = sessionSnapshot.GroupEntries
            };
            var document = loaded.Document;
            if (document is null || document.Nodes.Count == 0)
            {
                _documentService.Unload();
                BuildSampleGraph();
                return;
            }

            _documentService.Load(loaded);
            if (_session.IsDirty)
                _documentService.MarkDirty();

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
            project.IsDirty = isDirty || _session.IsDirty;
    }

    private CommandResult ExecuteSessionCommand(IProjectEditCommand command)
    {
        var result = _session.Execute(command);
        if (result.Success)
            OnCommandSucceeded();
        else
            Log.Warning("Editor command {CommandId} failed: {Diagnostics}", command.CommandId, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
        return result;
    }

    private CommandResult ExecuteLocalCommand(IProjectEditCommand command)
    {
        _ignoreSessionDocumentChanged = true;
        try
        {
            var result = _session.Execute(command, CreateMergeOptions(command));
            if (result.Success)
                OnCommandSucceeded();
            else
                Log.Warning("Editor command {CommandId} failed: {Diagnostics}", command.CommandId, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
            return result;
        }
        finally
        {
            _ignoreSessionDocumentChanged = false;
        }
    }

    private void OnCommandSucceeded()
    {
        _documentService.MarkDirty();
        if (_projectService.Current is { } project)
            project.IsDirty = _session.IsDirty;
        ScheduleAutoSave();
    }

    private void OnSessionDocumentChanged()
    {
        if (_ignoreSessionDocumentChanged || _disposed)
            return;
        CopySessionSettingsToCurrentProject();
        LoadCurrentProjectGraph();
    }

    private void OnSessionHistoryChanged()
    {
        if (_projectService.Current is { } project)
            project.IsDirty = _session.IsDirty;
    }

    private void OnTrackedNodeChanged(GraphNode node, string propertyName)
    {
        if (propertyName == nameof(GraphNode.Name))
            ExecuteLocalCommand(new RenameNodeCommand(node.Id, node.Name));
    }

    private void OnTrackedItemChanged(GraphNode node, object item, string propertyName)
    {
        IProjectEditCommand? command = item switch
        {
            EntryEditorItemViewModel entry when propertyName == nameof(EntryEditorItemViewModel.Type) =>
                new SetEntryTypeCommand(node.Id, entry.StableId, entry.Type),
            EntryEditorItemViewModel entry when propertyName == nameof(EntryEditorItemViewModel.Condition) =>
                new SetEntryConditionCommand(node.Id, entry.StableId, entry.Condition),
            EntryEditorItemViewModel entry when propertyName == nameof(EntryEditorItemViewModel.Parameters) =>
                new SetEntryParametersCommand(node.Id, entry.StableId, ParseEntryParameters(entry.Parameters)),
            BranchOptionEditorItemViewModel option when propertyName == nameof(BranchOptionEditorItemViewModel.Text) =>
                new SetChoiceOptionTextCommand(node.Id, option.Id, option.Text),
            BranchOptionEditorItemViewModel option when propertyName == nameof(BranchOptionEditorItemViewModel.Condition) =>
                new SetChoiceOptionConditionCommand(node.Id, option.Id, option.Condition),
            BranchConditionEditorItemViewModel condition when propertyName == nameof(BranchConditionEditorItemViewModel.Expression) =>
                new SetBranchConditionExpressionCommand(node.Id, condition.Id, condition.Expression),
            _ => null
        };
        if (command is not null)
            ExecuteLocalCommand(command);
    }

    private static ExecuteOptions? CreateMergeOptions(IProjectEditCommand command) => command switch
    {
        RenameNodeCommand value => new(MergeKey: $"node:{value.NodeId}:name"),
        SetEntryTypeCommand value => new(MergeKey: $"entry:{value.GroupId}:{value.EntryId}:type"),
        SetEntryConditionCommand value => new(MergeKey: $"entry:{value.GroupId}:{value.EntryId}:condition"),
        SetEntryParametersCommand value => new(MergeKey: $"entry:{value.GroupId}:{value.EntryId}:parameters"),
        SetChoiceOptionTextCommand value => new(MergeKey: $"option:{value.NodeId}:{value.OptionId}:text"),
        SetChoiceOptionConditionCommand value => new(MergeKey: $"option:{value.NodeId}:{value.OptionId}:condition"),
        SetBranchConditionExpressionCommand value => new(MergeKey: $"condition:{value.NodeId}:{value.ConditionId}:expression"),
        _ => null
    };

    private void CopySessionSettingsToCurrentProject()
    {
        if (_projectService.Current is not { } project)
            return;
        var source = _session.Document.Settings;
        var target = project.Settings;
        target.DefaultWidth = source.DefaultWidth;
        target.DefaultHeight = source.DefaultHeight;
        target.SaveSlotCount = source.SaveSlotCount;
        target.SfxChannelCount = source.SfxChannelCount;
        target.TargetLocale = new GalNet.Core.I18n.I18nLocale(source.TargetLocale.Code);
        target.AvailableLocales = source.AvailableLocales
            .Select(locale => new GalNet.Core.I18n.I18nLocale(locale.Code))
            .ToList();
        target.PlayerVariables = source.PlayerVariables.Select(item => item.Clone()).ToList();
        target.SaveVariables = source.SaveVariables.Select(item => item.Clone()).ToList();
    }

    private static IReadOnlyDictionary<string, string> ParseEntryParameters(string value) =>
        value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]))
            .ToDictionary(parts => parts[0], parts => parts.Length > 1 ? parts[1] : "", StringComparer.Ordinal);

    private void SynchronizeVariableDefinitionsToSession()
    {
        if (_isLoadingGraph || _ignoreSessionDocumentChanged)
            return;

        var commands = new List<IProjectEditCommand>();
        AppendVariableSynchronization(
            GalNet.Core.Variable.VariableScope.Player,
            _session.Document.Graph.PlayerVariables,
            _documentService.CurrentDocument.PlayerVariables,
            commands);
        AppendVariableSynchronization(
            GalNet.Core.Variable.VariableScope.Save,
            _session.Document.Graph.SaveVariables,
            _documentService.CurrentDocument.SaveVariables,
            commands);
        if (commands.Count == 0)
            return;

        _ignoreSessionDocumentChanged = true;
        try
        {
            var result = _session.ExecuteTransaction(commands);
            if (result.Success) OnCommandSucceeded();
            else Log.Warning("Variable synchronization failed: {Diagnostics}", string.Join("; ", result.Diagnostics.Select(item => item.Message)));
        }
        finally
        {
            _ignoreSessionDocumentChanged = false;
        }
    }

    private static void AppendVariableSynchronization(
        GalNet.Core.Variable.VariableScope scope,
        IReadOnlyList<GalNet.Core.Variable.ProjectVariableDefinition> persisted,
        IReadOnlyList<GalNet.Core.Variable.ProjectVariableDefinition> edited,
        ICollection<IProjectEditCommand> commands)
    {
        var editedByUid = edited.ToDictionary(item => item.DefaultValue.Uid, StringComparer.Ordinal);
        foreach (var oldItem in persisted.Where(item => !editedByUid.ContainsKey(item.DefaultValue.Uid)))
            commands.Add(new DeleteVariableDefinitionCommand(scope, oldItem.Name));

        var persistedByUid = persisted.ToDictionary(item => item.DefaultValue.Uid, StringComparer.Ordinal);
        var persistedIndexByUid = persisted.Select((item, index) => (item.DefaultValue.Uid, index))
            .ToDictionary(item => item.Uid, item => item.index, StringComparer.Ordinal);
        for (var index = 0; index < edited.Count; index++)
        {
            var item = edited[index];
            if (!persistedByUid.TryGetValue(item.DefaultValue.Uid, out var oldItem))
            {
                commands.Add(new AddVariableDefinitionCommand(
                    scope,
                    item.Name,
                    item.Type,
                    SerializeVariableValue(item),
                    index,
                    item.DefaultValue.Uid));
                continue;
            }
            var currentName = oldItem.Name;
            if (!string.Equals(currentName, item.Name, StringComparison.Ordinal))
            {
                commands.Add(new RenameVariableDefinitionCommand(scope, currentName, item.Name));
                currentName = item.Name;
            }
            if (oldItem.Type != item.Type)
                commands.Add(new SetVariableDefinitionTypeCommand(scope, currentName, item.Type));
            if (!VariableValuesEqual(oldItem, item))
                commands.Add(new SetVariableDefaultValueCommand(scope, currentName, SerializeVariableValue(item)));
            if (persistedIndexByUid[oldItem.DefaultValue.Uid] != index)
                commands.Add(new MoveVariableDefinitionCommand(scope, currentName, index));
        }
    }

    private static JsonElement SerializeVariableValue(GalNet.Core.Variable.ProjectVariableDefinition definition) =>
        JsonSerializer.SerializeToElement(definition.Type switch
        {
            GalNet.Core.Variable.VariableType.Bool => (object)definition.DefaultValue.AsBool(),
            GalNet.Core.Variable.VariableType.Int => definition.DefaultValue.AsInt(),
            GalNet.Core.Variable.VariableType.Float => definition.DefaultValue.AsFloat(),
            _ => definition.DefaultValue.AsString()
        });

    private static bool VariableValuesEqual(
        GalNet.Core.Variable.ProjectVariableDefinition left,
        GalNet.Core.Variable.ProjectVariableDefinition right) =>
        left.Type == right.Type && left.Type switch
        {
            GalNet.Core.Variable.VariableType.Bool => left.DefaultValue.AsBool() == right.DefaultValue.AsBool(),
            GalNet.Core.Variable.VariableType.Int => left.DefaultValue.AsInt() == right.DefaultValue.AsInt(),
            GalNet.Core.Variable.VariableType.Float => left.DefaultValue.AsFloat().Equals(right.DefaultValue.AsFloat()),
            _ => left.DefaultValue.AsString() == right.DefaultValue.AsString()
        };

    public bool AddEntryTo(GraphNode groupNode)
    {
        if (groupNode.NodeKind != GraphNodeKind.LinearGroup) return false;
        return ExecuteSessionCommand(new AddEntryCommand(
            groupNode.Id,
            Guid.NewGuid().ToString("N"),
            Type: "text",
            Parameters: new Dictionary<string, string> { ["speaker"] = "", ["text"] = "" })).Success;
    }

    public bool RemoveEntryFrom(GraphNode groupNode, EntryEditorItemViewModel entry) =>
        ExecuteSessionCommand(new DeleteEntryCommand(groupNode.Id, entry.StableId)).Success;

    public bool MoveEntryTo(GraphNode groupNode, EntryEditorItemViewModel entry, int newIndex) =>
        ExecuteSessionCommand(new MoveEntryCommand(groupNode.Id, entry.StableId, newIndex)).Success;

    public GraphNode? EntryNode => Nodes.FirstOrDefault(n => n.NodeKind == GraphNodeKind.Entry);
}

