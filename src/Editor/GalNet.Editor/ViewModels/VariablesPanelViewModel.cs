using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Core.Variable;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using GalNet.Editor.Services;

namespace GalNet.Editor.ViewModels;

public sealed record VariableDefinitionRow(string Uid, string Name, string Description, string Type);
public sealed record VariableTypeOption(VariableType Value, string LabelKey);

public sealed partial class VariablesPanelViewModel : ObservableObject, IUndoRedoTarget, IDisposable
{
    public AuthorNavigationService? Navigation { get; set; }
    [RelayCommand] private void NavigateReference(VariableReference? reference) { if (reference?.Target is { } target) Navigation?.Navigate(target); }
    private readonly VariableAuthoringService _authoring;
    private readonly IVariableDefinitionService _definitions;
    private readonly IEditorLocalizationService _localization;
    private readonly VariablePanelState _state;
    public IUndoRedoHistory UndoRedoHistory { get; }
    private ProjectVariableDefinition? _baseline;
    private bool _refreshing;
    private bool _applying;
    private bool _disposed;
    private EditorText? _message;
    public ObservableCollection<VariableDefinitionRow> Rows { get; } = [];
    public ObservableCollection<VariableReference> References { get; } = [];
    public IReadOnlyList<VariableTypeOption> Types { get; } = Enum.GetValues<VariableType>().Select(type => new VariableTypeOption(type, "Variables.Type." + type)).ToArray();
    [ObservableProperty] private int _scopeIndex;
    [ObservableProperty] private VariableDefinitionRow? _selected;
    [ObservableProperty] private string _draftName = "";
    [ObservableProperty] private string _draftDefault = "";
    [ObservableProperty] private string _draftDescription = "";
    [ObservableProperty] private VariableTypeOption? _draftType;
    [ObservableProperty] private string _messageText = "";
    [ObservableProperty] private string _search = "";
    public bool HasSelection => Selected is not null;
    public bool HasRows => Rows.Count > 0;
    public bool HasChanges => HasDraftChanges;
    public bool MissingSelection => _state.Selection.TryGetValue(Scope, out var uid) && _state.Drafts.ContainsKey((Scope, uid)) && !_authoring.Get(Scope).Any(item => item.DefaultValue.Uid == uid);
    public string ScopeHintKey => ScopeIndex == 0 ? "Variables.PlayerHint" : "Variables.SaveHint";
    public string DefaultHintKey => "Variables.DefaultHint." + (DraftType?.Value ?? VariableType.String);
    public bool IsBooleanDefault => DraftType?.Value == VariableType.Bool;
    public bool IsNumericDefault => DraftType?.Value is VariableType.Int or VariableType.Float;
    public bool IsTextDefault => DraftType?.Value == VariableType.String;
    public decimal DefaultIncrement => DraftType?.Value == VariableType.Int ? 1m : 0.1m;
    public bool DraftBoolean
    {
        get => bool.TryParse(DraftDefault, out var value) && value;
        set { if (IsBooleanDefault && !_refreshing) DraftDefault = value ? "true" : "false"; }
    }
    public string? NumericDefaultText
    {
        get => IsNumericDefault ? DraftDefault : "";
        set { if (IsNumericDefault && !_refreshing) DraftDefault = value ?? ""; }
    }
    public string? StringDefaultText
    {
        get => IsTextDefault ? DraftDefault : "";
        set { if (IsTextDefault && !_refreshing) DraftDefault = value ?? ""; }
    }
    private VariableScope Scope => ScopeIndex == 0 ? VariableScope.Player : VariableScope.Save;
    public VariablesPanelViewModel(VariableAuthoringService authoring, IVariableDefinitionService definitions, IEditorLocalizationService localization, EditorHistories histories, VariablePanelState state)
    {
        _authoring = authoring; _definitions = definitions; _localization = localization; _state = state; UndoRedoHistory = histories.Content;
        _refreshing = true; ScopeIndex = state.ScopeIndex; Search = state.Search; _refreshing = false;
        definitions.DefinitionsChanged += OnDefinitionsChanged;
        localization.PropertyChanged += OnLocaleChanged;
        Refresh();
    }
    partial void OnScopeIndexChanging(int value) { if (!_refreshing) SaveDraft(); }
    partial void OnScopeIndexChanged(int value)
    {
        if (_refreshing) return;
        _state.ScopeIndex = value; _refreshing = true; _baseline = null; Selected = null; _refreshing = false;
        Refresh(); OnPropertyChanged(nameof(ScopeHintKey)); _message = null; RenderMessage();
    }
    partial void OnSearchChanged(string value) { if (!_refreshing) { _state.Search = value; Refresh(); } }
    partial void OnSelectedChanging(VariableDefinitionRow? value) { if (!_refreshing) SaveDraft(); }
    partial void OnSelectedChanged(VariableDefinitionRow? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        if (!_refreshing)
        {
            if (value is not null) _state.Selection[Scope] = value.Uid;
            LoadDraft(); RefreshReferences(); _message = null; RenderMessage();
        }
    }
    partial void OnDraftTypeChanged(VariableTypeOption? value)
    {
        if (!_refreshing) DraftDefault = value?.Value switch { VariableType.Bool => "false", VariableType.Int or VariableType.Float => "0", _ => "" };
        OnPropertyChanged(nameof(DefaultHintKey));
        OnPropertyChanged(nameof(IsBooleanDefault));
        OnPropertyChanged(nameof(IsNumericDefault));
        OnPropertyChanged(nameof(IsTextDefault));
        OnPropertyChanged(nameof(DefaultIncrement));
        OnPropertyChanged(nameof(NumericDefaultText)); OnPropertyChanged(nameof(StringDefaultText));
        DraftChanged();
    }
    partial void OnDraftDefaultChanged(string value)
    { OnPropertyChanged(nameof(DraftBoolean)); OnPropertyChanged(nameof(NumericDefaultText)); OnPropertyChanged(nameof(StringDefaultText)); DraftChanged(); }
    partial void OnDraftNameChanged(string value) => DraftChanged();
    partial void OnDraftDescriptionChanged(string value) => DraftChanged();
    private void DraftChanged() { OnPropertyChanged(nameof(HasChanges)); }
    private bool HasDraftChanges => _baseline is not null && (DraftName != _baseline.Name || DraftDescription != _baseline.Description || DraftType?.Value != _baseline.Type || DraftDefault != VariableAuthoringService.DefaultText(_baseline));
    private void OnDefinitionsChanged(VariableScope _) => Refresh();
    private void Refresh()
    {
        if (_disposed) return;
        if (!_applying) SaveDraft();
        _state.Selection.TryGetValue(Scope, out var uid);
        _refreshing = true;
        try
        {
            Rows.Clear(); foreach (var value in _authoring.Get(Scope).Where(item => item.Name.Contains(Search, StringComparison.OrdinalIgnoreCase) || item.Description.Contains(Search, StringComparison.OrdinalIgnoreCase)))
                Rows.Add(new(value.DefaultValue.Uid, value.Name, value.Description, value.Type.ToString()));
            Selected = Rows.FirstOrDefault(row => row.Uid == uid);
            LoadDraft();
        }
        finally { _refreshing = false; }
        RefreshReferences();
        OnPropertyChanged(nameof(HasRows)); OnPropertyChanged(nameof(MissingSelection));
    }
    private void SaveDraft()
    {
        if (Selected is null || _baseline is null) return;
        var key = (Scope, Selected.Uid);
        if (HasDraftChanges) _state.Drafts[key] = new(_baseline.Clone(), DraftName, DraftType!.Value, DraftDefault, DraftDescription);
        else _state.Drafts.Remove(key);
    }
    private void LoadDraft()
    {
        var wasRefreshing = _refreshing; _refreshing = true;
        try
        {
            _baseline = _authoring.Get(Scope).FirstOrDefault(item => item.DefaultValue.Uid == Selected?.Uid)?.Clone();
            var draft = Selected is not null ? _state.Drafts.GetValueOrDefault((Scope, Selected.Uid)) : null;
            if (draft is not null) _baseline = draft.Baseline.Clone();
            DraftName = _baseline?.Name ?? ""; DraftDescription = _baseline?.Description ?? "";
            DraftType = Types.Single(item => item.Value == (_baseline?.Type ?? VariableType.String));
            DraftDefault = _baseline is null ? "" : VariableAuthoringService.DefaultText(_baseline);
            if (draft is not null)
            { DraftName = draft.Name; DraftDescription = draft.Description; DraftType = Types.Single(item => item.Value == draft.Type); DraftDefault = draft.Default; }
        }
        finally { _refreshing = wasRefreshing; }
        DraftChanged();
    }
    [RelayCommand]
    private void AddVariable() => Run(() =>
    {
        Search = "";
        var value = _authoring.Add(Scope); Refresh(); Selected = Rows.Single(row => row.Uid == value.DefaultValue.Uid);
    });
    [RelayCommand]
    private void Apply() => Run(() =>
    {
        if (Selected is null || _baseline is null) return;
        var uid = Selected.Uid;
        var warnings = _authoring.Update(Scope, uid, DraftName, DraftType!.Value, DraftDefault, DraftDescription, JsonSerializer.Serialize(_baseline));
        _state.Drafts.Remove((Scope, uid)); Search = ""; Refresh();
        if (warnings.Count > 0) { References.Clear(); foreach (var warning in warnings) References.Add(warning); _message = new("Variables.Candidates", warnings.Count); }
    });
    [RelayCommand] private void DeleteVariable() => Run(() => { if (Selected is not null) _authoring.Delete(Scope, Selected.Uid); });
    [RelayCommand]
    private void Revert()
    {
        if (Selected is not null) _state.Drafts.Remove((Scope, Selected.Uid));
        else if (_state.Selection.Remove(Scope, out var uid)) _state.Drafts.Remove((Scope, uid));
        LoadDraft(); _message = null; RenderMessage(); RefreshReferences(); OnPropertyChanged(nameof(MissingSelection));
    }
    [RelayCommand] private void CheckReferences() => RefreshReferences();
    private void RefreshReferences()
    {
        References.Clear();
        if (Selected is not null) foreach (var reference in _authoring.Inspect(Scope, Selected.Uid)) References.Add(reference);
    }
    private void Run(Action action)
    {
        if (_disposed) return;
        SaveDraft(); _applying = true; _message = null;
        try { action(); }
        catch (Exception failure) { _message = EditorText.Error(failure); }
        finally { _applying = false; RenderMessage(); }
    }
    private void OnLocaleChanged(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName == "Item[]") RenderMessage(); }
    private void RenderMessage() => MessageText = _message?.Render(_localization) ?? "";
    public void Dispose()
    {
        if (_disposed) return; SaveDraft(); _disposed = true;
        _definitions.DefinitionsChanged -= OnDefinitionsChanged; _localization.PropertyChanged -= OnLocaleChanged;
    }
}
