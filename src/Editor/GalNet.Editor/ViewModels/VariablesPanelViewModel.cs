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

public sealed record VariableDefinitionRow(string Uid, string Name, string Description);
public sealed record VariableTypeOption(VariableType Value, string LabelKey);

public sealed partial class VariablesPanelViewModel : ObservableObject, IUndoRedoTarget, IDisposable
{
    private readonly VariableAuthoringService _authoring;
    private readonly IVariableDefinitionService _definitions;
    private readonly IEditorLocalizationService _localization;
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
    public bool HasSelection => Selected is not null;
    public string ScopeHintKey => ScopeIndex == 0 ? "Variables.PlayerHint" : "Variables.SaveHint";
    public string DefaultHintKey => "Variables.DefaultHint." + (DraftType?.Value ?? VariableType.String);
    public bool IsBooleanDefault => DraftType?.Value == VariableType.Bool;
    public bool IsNumericDefault => DraftType?.Value is VariableType.Int or VariableType.Float;
    public bool IsTextDefault => DraftType?.Value == VariableType.String;
    public decimal DefaultIncrement => DraftType?.Value == VariableType.Int ? 1m : 0.1m;
    public bool DraftBoolean
    {
        get => bool.TryParse(DraftDefault, out var value) && value;
        set => DraftDefault = value ? "true" : "false";
    }
    private VariableScope Scope => ScopeIndex == 0 ? VariableScope.Player : VariableScope.Save;
    public VariablesPanelViewModel(VariableAuthoringService authoring, IVariableDefinitionService definitions, IEditorLocalizationService localization, EditorHistories histories)
    {
        _authoring = authoring; _definitions = definitions; _localization = localization; UndoRedoHistory = histories.Content;
        definitions.DefinitionsChanged += OnDefinitionsChanged;
        localization.PropertyChanged += OnLocaleChanged;
        Refresh();
    }
    partial void OnScopeIndexChanged(int value) { if (!_refreshing) { _baseline = null; Selected = null; Refresh(); OnPropertyChanged(nameof(ScopeHintKey)); } }
    partial void OnSelectedChanged(VariableDefinitionRow? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        if (!_refreshing) { LoadDraft(); RefreshReferences(); _message = null; RenderMessage(); }
    }
    partial void OnDraftTypeChanged(VariableTypeOption? value)
    {
        if (!_refreshing) DraftDefault = value?.Value switch { VariableType.Bool => "false", VariableType.Int or VariableType.Float => "0", _ => "" };
        OnPropertyChanged(nameof(DefaultHintKey));
        OnPropertyChanged(nameof(IsBooleanDefault));
        OnPropertyChanged(nameof(IsNumericDefault));
        OnPropertyChanged(nameof(IsTextDefault));
        OnPropertyChanged(nameof(DefaultIncrement));
    }
    partial void OnDraftDefaultChanged(string value) => OnPropertyChanged(nameof(DraftBoolean));
    private bool HasDraftChanges => _baseline is not null && (DraftName != _baseline.Name || DraftDescription != _baseline.Description || DraftType?.Value != _baseline.Type || DraftDefault != VariableAuthoringService.DefaultText(_baseline));
    private void OnDefinitionsChanged(VariableScope _) => Refresh();
    private void Refresh()
    {
        if (_disposed) return;
        var uid = Selected?.Uid; var preserve = !_applying && HasDraftChanges;
        _refreshing = true;
        try
        {
            Rows.Clear(); foreach (var value in _authoring.Get(Scope)) Rows.Add(new(value.DefaultValue.Uid, value.Name, value.Description));
            Selected = Rows.FirstOrDefault(row => row.Uid == uid);
            if (!preserve || Selected is null) LoadDraft();
        }
        finally { _refreshing = false; }
        RefreshReferences();
    }
    private void LoadDraft()
    {
        var wasRefreshing = _refreshing; _refreshing = true;
        try
        {
            _baseline = _authoring.Get(Scope).FirstOrDefault(item => item.DefaultValue.Uid == Selected?.Uid)?.Clone();
            DraftName = _baseline?.Name ?? ""; DraftDescription = _baseline?.Description ?? "";
            DraftType = Types.Single(item => item.Value == (_baseline?.Type ?? VariableType.String));
            DraftDefault = _baseline is null ? "" : VariableAuthoringService.DefaultText(_baseline);
        }
        finally { _refreshing = wasRefreshing; }
    }
    [RelayCommand]
    private void AddVariable() => Run(() =>
    {
        var value = _authoring.Add(Scope); Refresh(); Selected = Rows.Single(row => row.Uid == value.DefaultValue.Uid);
    });
    [RelayCommand]
    private void Apply() => Run(() =>
    {
        if (Selected is null || _baseline is null) return;
        var warnings = _authoring.Update(Scope, Selected.Uid, DraftName, DraftType!.Value, DraftDefault, DraftDescription, JsonSerializer.Serialize(_baseline));
        LoadDraft();
        if (warnings.Count > 0) { References.Clear(); foreach (var warning in warnings) References.Add(warning); _message = new("Variables.Candidates", warnings.Count); }
    });
    [RelayCommand] private void DeleteVariable() => Run(() => { if (Selected is not null) _authoring.Delete(Scope, Selected.Uid); });
    [RelayCommand] private void Revert() { LoadDraft(); _message = null; RenderMessage(); RefreshReferences(); }
    [RelayCommand] private void CheckReferences() => RefreshReferences();
    private void RefreshReferences()
    {
        References.Clear();
        if (Selected is not null) foreach (var reference in _authoring.Inspect(Scope, Selected.Uid)) References.Add(reference);
    }
    private void Run(Action action)
    {
        if (_disposed) return;
        _applying = true; _message = null;
        try { action(); }
        catch (Exception failure) { _message = EditorText.Error(failure); }
        finally { _applying = false; RenderMessage(); }
    }
    private void OnLocaleChanged(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName == "Item[]") RenderMessage(); }
    private void RenderMessage() => MessageText = _message?.Render(_localization) ?? "";
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _definitions.DefinitionsChanged -= OnDefinitionsChanged; _localization.PropertyChanged -= OnLocaleChanged;
    }
}
