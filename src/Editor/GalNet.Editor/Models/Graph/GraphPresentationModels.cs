using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.Parameters;
using GalNet.Core.Primitives;
using GalNet.Core.Scene;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Controls;
using GalNet.Editor.Services;
using GalNet.Game.Controls.Forms;
using GalNet.Primitives.Builtins;

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
    [ObservableProperty] private bool _isDebugCurrent;
    private static readonly IBrush SelectedBackground = Brush.Parse("#312A55");
    private static readonly IBrush DefaultBackground = Brush.Parse("#1E1E2E");
    private static readonly IBrush SelectedBorderBrush = Brush.Parse("#8F72FF");
    private static readonly IBrush EntryBorderBrush = Brush.Parse("#A891FF");
    private static readonly IBrush DefaultBorderBrush = Brush.Parse("#45475A");
    public EditorGraphNodeDto Source { get; }
    public GraphNodeKind NodeKind { get; }

    [ObservableProperty]
    private double _x;

    [ObservableProperty]
    private double _y;

    private string _name = "";
    public Action<string>? RenameRequested { get; set; }
    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            if (RenameRequested is { } rename) { rename(value); return; }
            if (SetProperty(ref _name, value)) Source.Name = value;
        }
    }
    private Action<string, string, string?>? _branchValueRequested;
    internal GameTextAuthoringService? GameTexts { get; set; }
    public Action<string, string, string?>? BranchValueRequested
    {
        get => _branchValueRequested;
        set { _branchValueRequested = value; foreach (var item in Options) item.ChangeRequested = value; foreach (var item in Conditions) item.ChangeRequested = value; }
    }
    private void RefreshBranchItems()
    {
        var options = Source.Options?.ToArray() ?? [];
        var conditions = Source.Conditions?.ToArray() ?? [];
        var changed = Options.Count != options.Length || !Options.Select(item => item.Id).SequenceEqual(options.Select(item => item.Id))
            || Conditions.Count != conditions.Length || !Conditions.Select(item => item.Id).SequenceEqual(conditions.Select(item => item.Id));
        foreach (var item in Options.Where(item => !options.Any(dto => ReferenceEquals(dto, item.Source))).ToArray()) { item.ChangeRequested = null; Options.Remove(item); }
        foreach (var item in Conditions.Where(item => !conditions.Any(dto => ReferenceEquals(dto, item.Source))).ToArray()) { item.ChangeRequested = null; Conditions.Remove(item); }
        for (var i = 0; i < options.Length; i++)
        {
            var item = Options.FirstOrDefault(item => ReferenceEquals(item.Source, options[i]));
            if (item is null) { item = new(options[i]); Options.Insert(i, item); }
            else if (Options.IndexOf(item) != i) Options.Move(Options.IndexOf(item), i);
            item.ChangeRequested = BranchValueRequested; item.BindText(GameTexts, Id); item.Refresh();
        }
        for (var i = 0; i < conditions.Length; i++)
        {
            var item = Conditions.FirstOrDefault(item => ReferenceEquals(item.Source, conditions[i]));
            if (item is null) { item = new(conditions[i]); Conditions.Insert(i, item); }
            else if (Conditions.IndexOf(item) != i) Conditions.Move(Conditions.IndexOf(item), i);
            item.ChangeRequested = BranchValueRequested; item.Refresh();
        }
        if (changed) RefreshConnectors();
    }
    public void RefreshAuthorValues()
    {
        SetProperty(ref _name, Source.Name, nameof(Name));
        X = Source.X;
        Y = Source.Y;
        RefreshBranchItems();
    }

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

    public string Id => Source.Id;
    public string KindLabelKey => "Graph.Kind." + NodeKind;
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
    public ObservableCollection<EntryEditorItemViewModel> Entries { get; private set; } = [];
    public ObservableCollection<BranchOptionEditorItemViewModel> Options { get; } = [];
    public ObservableCollection<BranchConditionEditorItemViewModel> Conditions { get; } = [];

    public int EntryCount => Entries.Count;
    public double LayoutHeight => GraphLayoutMetrics.GetNodeHeight(InputConnectors.Count, OutputConnectors.Count);

    public GraphNode(Node node, GraphNodeKind nodeKind) : this(new EditorGraphNodeDto
    {
        Id = node.Id,
        Name = node.Name,
        Type = nodeKind == GraphNodeKind.Entry ? "Entry" : nodeKind == GraphNodeKind.LinearGroup ? "Group" : "Branch",
        File = nodeKind == GraphNodeKind.LinearGroup ? $"groups/{node.Id}.rawgalgroup" : null,
        BranchType = nodeKind == GraphNodeKind.ChoiceBranch ? "Choice" : "Condition"
    }, nodeKind)
    { }

    public GraphNode(EditorGraphNodeDto source, GraphNodeKind nodeKind)
    {
        Source = source;
        NodeKind = nodeKind;
        _name = source.Name;
        _x = source.X;
        _y = source.Y;

        foreach (var option in source.Options ?? []) Options.Add(new BranchOptionEditorItemViewModel(option));
        foreach (var condition in source.Conditions ?? []) Conditions.Add(new BranchConditionEditorItemViewModel(condition));
        Options.CollectionChanged += (_, _) => Source.Options = Options.Select(item => item.Source).ToList();
        Conditions.CollectionChanged += (_, _) => Source.Conditions = Conditions.Select(item => item.Source).ToList();

        RefreshConnectors();
    }

    public void BindGroup(EditorGroupProjection group)
    {
        if (Entries.Count != 0) throw new InvalidOperationException("Group must be bound before inserting entries.");
        Entries = group.Entries;
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
            GraphNodeKind.ChoiceBranch => Options.Count,
            GraphNodeKind.ConditionBranch => Conditions.Count,
            _ => 1
        };

        for (var i = 0; i < outputCount; i++)
            OutputConnectors.Add(new GraphConnector(this, GraphConnectorKind.Output, i));

        OnPropertyChanged(nameof(EntryCount));
        OnPropertyChanged(nameof(LayoutHeight));
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


}

public partial class GraphConnector : ObservableObject
{
    public GraphNode Node { get; }
    public GraphConnectorKind Kind { get; }
    public string? ItemId { get; }
    private readonly int _index;
    public int Index => ItemId is null ? _index : Node.NodeKind == GraphNodeKind.ChoiceBranch
        ? Node.Options.ToList().FindIndex(item => item.Id == ItemId) : Node.Conditions.ToList().FindIndex(item => item.Id == ItemId);

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isPreviewTarget;

    public GraphConnector(GraphNode node, GraphConnectorKind kind, int index)
    {
        Node = node;
        Kind = kind;
        _index = index;
        ItemId = kind != GraphConnectorKind.Output ? null : node.NodeKind == GraphNodeKind.ChoiceBranch ? node.Options.ElementAtOrDefault(index)?.Id
            : node.NodeKind == GraphNodeKind.ConditionBranch ? node.Conditions.ElementAtOrDefault(index)?.Id : null;
    }
}

public partial class GraphEdge : ObservableObject, IDisposable
{
    private static readonly IBrush DefaultStrokeBrush = Brush.Parse("#8F72FF");
    private static readonly IBrush SelectedStrokeBrush = Brush.Parse("#A891FF");
    public GraphNode From { get; }
    public GraphNode To { get; }
    public EditorGraphEdgeDto Source { get; }
    public string Id => Source.Id;

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

    public GraphEdge(GraphNode from, GraphNode to, int outlet = 0, string? id = null) : this(from, to, new EditorGraphEdgeDto
    {
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id,
        FromNodeId = from.Id,
        ToNodeId = to.Id,
        FromOutlet = outlet
    })
    { }

    public GraphEdge(GraphNode from, GraphNode to, EditorGraphEdgeDto source)
    {
        Source = source;
        From = from;
        To = to;
        _outlet = source.FromOutlet;

        From.PropertyChanged += NodeMoved;
        To.PropertyChanged += NodeMoved;
        From.OutputConnectors.CollectionChanged += ConnectorsChanged;
        To.InputConnectors.CollectionChanged += ConnectorsChanged;
    }

    private void NodeMoved(object? sender, System.ComponentModel.PropertyChangedEventArgs args) => OnNodeMoved(args.PropertyName);
    private void ConnectorsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs args) => OnNodeMoved(nameof(GraphNode.LayoutHeight));
    public void Dispose()
    {
        From.PropertyChanged -= NodeMoved;
        To.PropertyChanged -= NodeMoved;
        From.OutputConnectors.CollectionChanged -= ConnectorsChanged;
        To.InputConnectors.CollectionChanged -= ConnectorsChanged;
        GC.SuppressFinalize(this);
    }

    private void OnNodeMoved(string? propertyName)
    {
        if (propertyName is not (nameof(GraphNode.X) or nameof(GraphNode.Y) or nameof(GraphNode.LayoutHeight)))
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
        Source.FromOutlet = value;
        OnPropertyChanged(nameof(StartX));
        OnPropertyChanged(nameof(StartY));
        OnPropertyChanged(nameof(PathData));
    }
}

public partial class EntryEditorItemViewModel : ObservableObject, IDisposable
{
    internal EntryEditSession? EditSession { get; set; }
    [ObservableProperty] private bool _isDebugBreakpoint;
    [ObservableProperty] private bool _isDebugCurrent;
    internal string? AuthorGroupId => _groupId;
    public EditorEntryData Source { get; }
    private IEntryCatalog? _authorCatalog;
    private bool _disposed;
    public string StableId { get => Source.StableId; init => Source.StableId = value; }
    [ObservableProperty] private bool _isParametersExpanded = true;

    public EntryEditorItemViewModel() => Source = new() { StableId = Guid.NewGuid().ToString("N") };

    public EntryEditorItemViewModel(IEntryCatalog catalog) : this() => _authorCatalog = catalog;

    public EntryEditorItemViewModel(EditorEntryData source, IEntryCatalog catalog)
    {
        Source = source;
        _authorCatalog = catalog;
        _id = source.Id;
        _type = source.Type;
        _condition = source.Condition;
        _parameters = source.GetParameterText(catalog);
    }

    private EntryFieldAuthoringService? _authoring;
    private string? _groupId;
    private SceneTargetCatalog? _scenes;
    private int _schemaGeneration;
    internal Guid DraftIncarnation { get; private set; } = Guid.NewGuid();
    internal void BindAuthoring(string groupId, EntryFieldAuthoringService authoring, SceneTargetCatalog? scenes) { _groupId = groupId; _authoring = authoring; _scenes = scenes; }
    internal IReadOnlyList<FieldEditorOption>? SceneOptions(string field) => _scenes?.FieldOptions(_groupId!, StableId, Type, field);
    internal bool IsSceneOutput(string field) => SceneFieldSemantics.IsOutput(Type, field);
    internal Type? ParameterDtoType => _authorCatalog?.TryGet(Type, out var schema) == true ? schema.FieldDataType : null;
    internal bool IsCurrent => !_disposed && (_authoring is null || _authoring.IsCurrent(_groupId!, Source));
    internal void RefreshAuthorReferences(IEntryCatalog catalog)
    {
        SetProperty(ref _id, Source.Id, nameof(Id));
        var values = Source.GetParameterText(catalog);
        var parametersChanged = !_parameters.OrderBy(pair => pair.Key).SequenceEqual(values.OrderBy(pair => pair.Key));
        _parameters = values;
        if (_type != Source.Type) InvalidateAuthorBindings();
        SetProperty(ref _type, Source.Type, nameof(Type));
        SetProperty(ref _condition, Source.Condition, nameof(Condition));
        if (parametersChanged) OnPropertyChanged(nameof(Parameters));
        OnPropertyChanged(nameof(HasUnknownParameters));
        foreach (var field in ParameterFields) field.RefreshValue(Parameters.GetValueOrDefault(field.Id, field.Definition.DefaultValue));
        ParameterForm?.Refresh();
        _detailForm?.Refresh();
        OnPropertyChanged(nameof(DetailForm));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(HasCondition));
        UpdateEffectHints();
    }

    partial void OnIdChanged(int value) => Source.Id = value;
    [ObservableProperty] private int _id;
    private string _type = "";
    private string _condition = "";
    private Dictionary<string, string> _parameters = new(StringComparer.Ordinal);
    public string Type
    {
        get => Source.Type;
        set
        {
            if (Type == value) return;
            if (_authoring is not null) ChangeAuthorType(value, _authorCatalog!.Create(value).Values);
            else { Source.Type = value; SetProperty(ref _type, value); }
        }
    }
    internal void ChangeAuthorType(string type, IReadOnlyDictionary<string, string> parameters) =>
        _authoring!.SetType(_groupId!, Source, type, EditorEntryData.ParseParameters(_authorCatalog!, type, parameters));
    public string Condition
    {
        get => Source.Condition;
        set
        {
            value ??= "";
            if (Condition == value) return;
            if (_authoring is not null) _authoring.SetCondition(_groupId!, Source, value);
            else { Source.Condition = value; SetProperty(ref _condition, value); }
        }
    }
    /// <summary>Text is a projection of author JSON, never a second writable document.</summary>
    public Dictionary<string, string> Parameters
    {
        get => _authorCatalog is null ? new(_parameters, StringComparer.Ordinal) : Source.GetParameterText(_authorCatalog);
        set
        {
            if (_authorCatalog is null) { SetProperty(ref _parameters, new(value, StringComparer.Ordinal)); return; }
            var parsed = EditorEntryData.ParseParameters(_authorCatalog, Type, value);
            if (_authoring is not null) _authoring.SetParameters(_groupId!, Source, Type, parsed);
            else { Source.Parameters = parsed; RefreshAuthorReferences(_authorCatalog); }
        }
    }

    [ObservableProperty]
    private string _effectHint = "";

    [ObservableProperty]
    private string _effectDiagnostic = "";

    private IEffectCatalog? _effectCatalog;
    private DynamicParameterTable? _formParameters;
    [ObservableProperty] private EntryParameterFormBinding? _parameterForm;
    private EntryParameterFormBinding? _detailForm;
    public EntryParameterFormBinding? DetailForm => ParameterForm ?? _detailForm;
    public string Summary => EditSession?.Summary(this) ?? string.Join(" · ", Source.Parameters.Where(pair => pair.Key is "content" or "speaker" or "asset" or "assetId" or "target" or "expression" or "handleId")
        .Select(pair => pair.Value.ValueKind == JsonValueKind.String ? pair.Value.GetString() : pair.Value.GetRawText()).Where(value => !string.IsNullOrEmpty(value))).Replace('\n', ' ');
    public bool HasCondition => !string.IsNullOrWhiteSpace(Condition);
    [ObservableProperty] private int _problemCount;
    [ObservableProperty] private bool _isPendingMove;
    public bool HasProblems => ProblemCount > 0;
    partial void OnProblemCountChanged(int value) => OnPropertyChanged(nameof(HasProblems));
    public bool HasParameterForm => ParameterForm is not null;
    partial void OnParameterFormChanged(EntryParameterFormBinding? value) => OnPropertyChanged(nameof(HasParameterForm));
    public bool HasEffectDiagnostic => !string.IsNullOrWhiteSpace(EffectDiagnostic);

    public ObservableCollection<EntryParameterEditorItemViewModel> ParameterFields { get; } = [];
    public bool IsUnknownType => _authorCatalog is not null && !_authorCatalog.TryGet(Type, out _);
    public bool HasUnknownParameters => _authorCatalog is not null && _authorCatalog.TryGet(Type, out var schema) && Source.Parameters.Keys.Any(key => !schema.Parameters.ContainsKey(key));
    /// <summary>Builds the editor projection from the Core registry, the single schema source.</summary>
    public void ConfigureParameterFields(IEntryCatalog catalog, IReadOnlyList<string> speakers, IReadOnlyList<string> variableNames, IEffectCatalog? effectCatalog = null)
    {
        if (_authorCatalog is null)
            Source.Parameters = EditorEntryData.ParseParameters(catalog, Type, Parameters);
        _authorCatalog = catalog;
        ParameterForm?.Dispose();
        ParameterForm = null;
        _detailForm?.Dispose(); _detailForm = null;
        _formParameters = null;
        _effectCatalog = effectCatalog;
        _schemaGeneration++;
        ParameterFields.Clear();
        OnPropertyChanged(nameof(IsUnknownType));
        OnPropertyChanged(nameof(HasUnknownParameters));
        if (!catalog.TryGet(Type, out var schema)) { UpdateEffectHints(); return; }
        var parameters = schema.DynamicParameters ?? EntrySchema.DynamicParameters(schema.Parameters, schema.Defaults, schema.Options);
        _detailForm = new EntryParameterFormBinding(this, parameters.Fields, schema.FieldDataType);
        OnPropertyChanged(nameof(DetailForm));

        if (schema.FieldDataType is not null)
        {
            _formParameters = parameters;
            if (!schema.Parameters.Any(pair => UsesDedicatedEditor(parameters[pair.Key].Field, pair.Value)))
            {
                ParameterForm = new EntryParameterFormBinding(this, parameters.Fields, schema.FieldDataType);
                UpdateEffectHints();
                return;
            }
        }

        foreach (var (id, type) in schema.Parameters)
        {
            var defaultValue = schema.Defaults.GetValueOrDefault(id, "");
            var options = (Type == BuiltinEntryIds.ApplyEffect && id == "id" && effectCatalog is not null
                    ? effectCatalog.Definitions.Select(definition => definition.Id).ToArray()
                    : schema.Options.GetValueOrDefault(id, []))
                .Select(value => new EntrySelectOption(value, $"Entry.Option.{(value.Length == 0 ? "None" : value)}"))
                .ToArray();
            var source = parameters[id].Field;
            var field = Type == BuiltinEntryIds.ApplyEffect && id == "id" && effectCatalog is not null
                ? new FieldDescriptor(id, source.Value, source.IsRequired, source.DefaultValue, new(ValueEditorIds.Choice), source.LabelKey, source.DescriptionKey) : source;
            var definition = new EntryParameterDefinition(id, type, $"Entry.Parameter.{id}", defaultValue, options, field);
            var value = Parameters.GetValueOrDefault(id, defaultValue);
            ParameterFields.Add(CreateField(definition, value, speakers, variableNames));
        }
        UpdateEffectHints();
    }

    internal void InvalidateAuthorBindings()
    {
        DraftIncarnation = Guid.NewGuid();
        InvalidateParameterFields();
    }
    internal void InvalidateParameterFields()
    {
        _detailForm?.Dispose(); _detailForm = null;
        _schemaGeneration++;
        ParameterForm?.Dispose();
        ParameterForm = null;
        ParameterFields.Clear();
        OnPropertyChanged(nameof(DetailForm));
    }

    private static bool UsesDedicatedEditor(FieldDescriptor field, EntryParameterType type) =>
        field.Semantic == FieldValueSemantic.None && field.Value.ValueType == typeof(string) && type is (EntryParameterType.MultilineText or EntryParameterType.Autocomplete
            or EntryParameterType.VariableName or EntryParameterType.Expression or EntryParameterType.ImageAsset or EntryParameterType.AudioAsset
            or EntryParameterType.VideoAsset or EntryParameterType.EffectProgramAsset)
            && field.Editor?.EditorId == "entry." + type.ToString().ToLowerInvariant();

    private EntryParameterEditorItemViewModel CreateField(EntryParameterDefinition definition, string value, IReadOnlyList<string> speakers, IReadOnlyList<string> variableNames)
    {
        var generation = _schemaGeneration;
        var type = Type;
        void Commit(string id, string text) { if (generation == _schemaGeneration && Type == type && IsCurrent) SetParameter(id, text); }
        EntryParameterEditorItemViewModel result =
        !UsesDedicatedEditor(definition.Field, definition.Type)
        ? new SchemaEntryParameterEditorItemViewModel(definition, this, Commit) : definition.Type switch
        {
            EntryParameterType.Autocomplete => new AutocompleteEntryParameterEditorItemViewModel(definition, value, speakers, Commit),
            EntryParameterType.VariableName => new VariableNameEntryParameterEditorItemViewModel(definition, value, variableNames, Commit),
            EntryParameterType.Expression => new ExpressionEntryParameterEditorItemViewModel(definition, value, variableNames, Commit),
            EntryParameterType.ImageAsset or EntryParameterType.AudioAsset or EntryParameterType.VideoAsset or EntryParameterType.EffectProgramAsset => new AssetEntryParameterEditorItemViewModel(definition, value, speakers, Commit),
            _ => new TextEntryParameterEditorItemViewModel(definition, value, speakers, Commit)
        };
        result.ActionForm = new(this, [definition.Field], ParameterDtoType, () => generation == _schemaGeneration);
        return result;
    }

    private void SetParameter(string id, string value)
    {
        var updated = Source.Parameters.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);
        var field = _authorCatalog!.Get(Type).DynamicParameters![id].Field;
        if (value.Length == 0 && (!field.IsRequired || field.DefaultValue is not null) &&
            field.Editor?.EditorId is "entry.imageasset" or "entry.audioasset" or "entry.videoasset" or "entry.effectprogramasset") updated.Remove(id);
        else updated[id] = ValueCodec.ParseEditorText(field.Value, value);
        if (_authoring is not null) _authoring.SetParameters(_groupId!, Source, Type, updated);
        else { Source.Parameters = updated; RefreshAuthorReferences(_authorCatalog); }
    }

    internal Task<ValueIssue?> CommitParameterAsync(FieldDescriptor field, FieldValue value, CancellationToken cancellationToken)
        => CommitParametersAsync(new Dictionary<string, (FieldDescriptor Field, FieldValue Value)> { [field.Id] = (field, value) }, cancellationToken);

    internal async Task<ValueIssue?> CommitParametersAsync(IReadOnlyDictionary<string, (FieldDescriptor Field, FieldValue Value)> patch, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCurrent) return new("entry.obsolete", "The project document is closed.");
        var updated = Source.Parameters.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);
        foreach (var (id, (field, value)) in patch)
        {
            if (value.Json is not { } json)
            {
                if (field.IsRequired && field.DefaultValue is null) return new("entry.required", "A value is required.", id);
                updated.Remove(id);
            }
            else
            {
                if (ValueValidation.Validate(field.Value, json) is { } issue) return issue;
                updated[id] = json.Clone();
            }
        }
        try
        {
            if (_formParameters is { } parameters)
                parameters.ValidatePartialArguments(JsonSerializer.SerializeToElement(updated.Where(pair => patch.ContainsKey(pair.Key) && parameters.Fields.Any(field => field.Id == pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value)));
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException)
        { return new("entry.arguments", exception.Message); }
        if (_authoring is not null) await _authoring.SetParametersAsync(_groupId!, Source, Type, updated, cancellationToken);
        else { Source.Parameters = updated; RefreshAuthorReferences(_authorCatalog!); }
        return null;
    }

    partial void OnEffectDiagnosticChanged(string value) => OnPropertyChanged(nameof(HasEffectDiagnostic));

    private void UpdateEffectHints()
    {
        if (Type != BuiltinEntryIds.ApplyEffect || _effectCatalog is null)
        {
            EffectHint = "";
            EffectDiagnostic = "";
            return;
        }
        var id = Parameters.GetValueOrDefault("id", "");
        if (!_effectCatalog.TryGet(id, out var effect))
        {
            EffectHint = "";
            EffectDiagnostic = string.IsNullOrWhiteSpace(id) ? "Select an effect type." : $"Unknown effect '{id}'.";
            return;
        }
        EffectHint = $"{effect.Stage} · static: {string.Join(", ", effect.Parameters.Select(parameter => parameter.Name))} · animatable: {string.Join(", ", effect.AnimatableProperties.Select(property => property.Name))}";
        EffectDiagnostic = string.Join(" ", _effectCatalog.Validate(id, Parameters.GetValueOrDefault("targetHandleId", ""), Parameters.GetValueOrDefault("parameters", "{}")));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _detailForm?.Dispose();
        ParameterForm?.Dispose();
        ParameterFields.Clear();
        GC.SuppressFinalize(this);
    }
}

public partial class BranchOptionEditorItemViewModel : ObservableObject
{
    public GameTextAuthoringService? GameTexts { get; private set; }
    public IFieldBinding? TextBinding { get; private set; }
    internal void BindText(GameTextAuthoringService? service, string node)
    {
        if (ReferenceEquals(GameTexts, service)) return;
        GameTexts = service; TextBinding = service?.BranchBinding(node, Source);
        OnPropertyChanged(nameof(GameTexts)); OnPropertyChanged(nameof(TextBinding));
    }
    public EditorGraphBranchOptionDto Source { get; }
    public string Id { get => Source.Id; init => Source.Id = value; }
    public Action<string, string, string?>? ChangeRequested { get; set; }
    private string _text = "", _condition = "";
    public string Text { get => _text; set { if (_text == value) return; if (ChangeRequested is { } change) change(Id, nameof(Text), value); else { Source.Text = value ?? ""; Refresh(); } } }
    public string Condition { get => _condition; set { if (_condition == value) return; if (ChangeRequested is { } change) change(Id, nameof(Condition), value); else { Source.Condition = value ?? ""; Refresh(); } } }
    public BranchOptionEditorItemViewModel() : this(new() { Id = Guid.NewGuid().ToString("N") }) { }
    public BranchOptionEditorItemViewModel(EditorGraphBranchOptionDto source) { Source = source; Refresh(); }
    public void Refresh() { SetProperty(ref _text, Source.Text, nameof(Text)); SetProperty(ref _condition, Source.Condition, nameof(Condition)); }
}

public partial class BranchConditionEditorItemViewModel : ObservableObject
{
    public EditorGraphBranchConditionDto Source { get; }
    public string Id { get => Source.Id; init => Source.Id = value; }
    public Action<string, string, string?>? ChangeRequested { get; set; }
    private string _expression = "";
    public string Expression { get => _expression; set { if (_expression == value) return; if (ChangeRequested is { } change) change(Id, nameof(Expression), value); else { Source.Expression = value ?? ""; Refresh(); } } }
    public BranchConditionEditorItemViewModel() : this(new() { Id = Guid.NewGuid().ToString("N") }) { }
    public BranchConditionEditorItemViewModel(EditorGraphBranchConditionDto source) { Source = source; Refresh(); }
    public void Refresh() => SetProperty(ref _expression, Source.Expression, nameof(Expression));
}
