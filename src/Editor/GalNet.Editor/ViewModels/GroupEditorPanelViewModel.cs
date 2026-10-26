using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Contracts.Builtins;
using GalNet.Core.Assets;
using GalNet.Core.Entry;
using GalNet.Core.Scene;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Commands;
using GalNet.Editor.Controls;
using GalNet.Editor.History;
using GalNet.Editor.Services;
using GalNet.Editor.Shared.Services;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.ViewModels;

public partial class GroupEditorPanelViewModel : ObservableObject, IUndoRedoTarget, IDisposable
{
    private readonly GroupEntryAuthoringService _authoring;
    private readonly IProjectService _projects;
    private readonly IEffectCatalog _effects;
    private readonly IEntryCatalog _catalog;
    private readonly EntrySelectionContext _selection;
    private readonly Guid _selectionOwner = Guid.NewGuid();
    private readonly IEditorLocalizationService? _localization;
    private EditorText? _errorMessage;
    private bool _renderingError;

    public EditorWorkspaceViewModel Workspace { get; }
    public GraphNode GroupNode => Workspace.Nodes.Single(node => node.Id == GroupId);
    public IUndoRedoHistory? UndoRedoHistory => Workspace.UndoRedoHistory;

    public string GroupId { get; }
    private readonly ObservableCollection<EntryEditorItemViewModel> _entries;
    private bool _disposed;
    private readonly HashSet<EntryEditorItemViewModel> _attached = [];
    private readonly Dictionary<string, bool> _expanded = new(StringComparer.Ordinal);
    private (string? Anchor, int Count)? _pendingInsertion;
    public event Action? TypeSelectionRequested;
    public string? PickerAnchor => _pendingInsertion.HasValue ? _pendingInsertion.Value.Anchor : SelectedEntry?.StableId;
    public void CancelInsertion() => _pendingInsertion = null;
    [ObservableProperty] private string _insertionType = BuiltinEntryIds.Text;
    [ObservableProperty] private string _errorText = "";
    partial void OnErrorTextChanged(string value) { if (!_renderingError) _errorMessage = null; }
    public void ReportError(Exception error)
    {
        _errorMessage = EditorText.Error(error);
        RenderError();
        Workspace.ReportGraphError(error);
    }
    private void RenderError()
    {
        if (_errorMessage is null) return;
        _renderingError = true;
        try { ErrorText = _localization is null ? _errorMessage.Key ?? "" : _errorMessage.Render(_localization); }
        finally { _renderingError = false; }
    }
    private void OnLocaleChanged(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName == "Item[]") { RenderError(); OnPropertyChanged(nameof(MoveStatus)); } }
    public IReadOnlyList<ConditionVariableSuggestion> ConditionSuggestions => Workspace.GetConditionVariableSuggestions();
    public IReadOnlyList<GalNet.Core.Variable.ProjectVariableDefinition> ValidationVariables => Workspace.AllProjectVariableDefinitions;
    public IAssetManager AssetManager { get; }
    public EditorShortcutService ShortcutService { get; }
    public IReadOnlyList<EntryTypeOption> EntryTypes { get; }
    public EditorRegistry FieldEditors { get; }
    [ObservableProperty] private string _searchText = "";
    public IEnumerable<EntryEditorItemViewModel> VisibleEntries => string.IsNullOrWhiteSpace(SearchText) ? GroupNode.Entries : GroupNode.Entries.Where(entry =>
        entry.Id.ToString(System.Globalization.CultureInfo.InvariantCulture).Equals(SearchText, StringComparison.OrdinalIgnoreCase) || entry.Type.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
        || entry.Summary.Contains(SearchText, StringComparison.OrdinalIgnoreCase) || entry.Condition.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
    partial void OnSearchTextChanged(string value) => OnPropertyChanged(nameof(VisibleEntries));
    [RelayCommand] private void ExpandAll() { foreach (var entry in GroupNode.Entries) entry.IsParametersExpanded = true; }
    [RelayCommand] private void CollapseAll() { foreach (var entry in GroupNode.Entries) entry.IsParametersExpanded = false; }
    [RelayCommand]
    private void ShowEntryProblems(EntryEditorItemViewModel? entry)
    {
        if (entry is not null) SelectedEntry = entry;
        (_projects.Current?.Services.GetService(typeof(GalNet.Editor.Dock.EditorDockFactory)) as GalNet.Editor.Dock.EditorDockFactory)?.OpenPanel(GalNet.Editor.Dock.EditorDockPanelIds.Problems);
    }

    [ObservableProperty]
    private EntryEditorItemViewModel? _selectedEntry;

    public ObservableCollection<EntryEditorItemViewModel> SelectedEntries { get; } = [];
    private bool _changingSelection;
    public void SetSelection(IEnumerable<EntryEditorItemViewModel> entries)
    {
        var selected = entries.Where(_entries.Contains).Distinct().ToArray();
        _changingSelection = true;
        try
        {
            SelectedEntries.Clear();
            foreach (var entry in selected) SelectedEntries.Add(entry);
            SelectedEntry = selected.LastOrDefault();
        }
        finally { _changingSelection = false; }
        PublishSelection();
    }
    partial void OnSelectedEntryChanged(EntryEditorItemViewModel? value)
    {
        if (!_changingSelection) SetSelection(value is null ? [] : [value]);
        PublishSelection();
    }
    public void UpdateActiveSelection(EntryEditorItemViewModel? added)
    {
        if (_changingSelection) return;
        _changingSelection = true;
        try { SelectedEntry = added ?? (SelectedEntry is not null && SelectedEntries.Contains(SelectedEntry) ? SelectedEntry : SelectedEntries.LastOrDefault()); }
        finally { _changingSelection = false; }
        PublishSelection();
    }
    public bool IsDisposed => _disposed;
    public void PublishSelection() { if (!_disposed) _selection.Select(_selectionOwner, GroupId, SelectedEntry?.StableId); }
    public string CopySelection() => _authoring.Copy(GroupId, SelectedEntries.Select(entry => entry.StableId));
    public EntryCutOffer? PrepareCutSelection() => _authoring.PrepareCut(GroupId, SelectedEntries.Select(entry => entry.StableId));
    public void BeginCut(EntryCutOffer offer) { if (!_disposed) { _authoring.CancelPendingMove(); _authoring.BeginCut(offer); } }
    public bool HasPendingMove => _authoring.HasPendingMove;
    private bool _copiedMove;
    public string MoveStatus => HasPendingMove ? _localization?["Entry.MovePending"] ?? "" : _copiedMove ? _localization?["Entry.MoveFallback"] ?? "" : "";
    [RelayCommand] public void CancelPendingMove() { _authoring.CancelPendingMove(); _copiedMove = false; OnPropertyChanged(nameof(MoveStatus)); }
    private void PendingMoveChanged()
    {
        foreach (var entry in _entries) entry.IsPendingMove = _authoring.IsPendingMove(GroupId, entry.StableId);
        OnPropertyChanged(nameof(HasPendingMove)); OnPropertyChanged(nameof(MoveStatus));
    }
    public void DeleteEntries(IEnumerable<string> ids) { if (!_disposed) _authoring.Delete(GroupId, ids); }
    public void DeleteSelection() => DeleteEntries(SelectedEntries.Select(entry => entry.StableId).ToArray());
    public Func<SceneEditImpact, Task<bool>>? ReviewImpactAsync { get; set; }
    private async Task CommitReviewedAsync(PreparedEntryEdit? edit)
    {
        if (edit is not null && await ReviewAsync(edit)) edit.Commit();
    }
    private async Task<bool> ReviewAsync(PreparedEntryEdit edit) => !_disposed && (!edit.Impact.RequiresReview ||
        await (ReviewImpactAsync?.Invoke(edit.Impact) ?? SceneEntryImpactPrompt.ConfirmAsync(edit.Impact, _localization, Describe))) && !_disposed;
    private string Describe(SceneAuthorAddress address)
    {
        var node = Workspace.Nodes.FirstOrDefault(item => item.Id == address.GroupId);
        var entry = node?.Entries.FirstOrDefault(item => item.StableId == address.EntryId);
        return (node?.Name ?? address.GroupId) + " / " + entry?.Id + " · " + Workspace.Localize("Entry.Type." + entry?.Type);
    }
    public Task DeleteEntriesAsync(IEnumerable<string> ids) => _disposed ? Task.CompletedTask : CommitReviewedAsync(_authoring.PrepareDelete(GroupId, ids));
    public Task DeleteSelectionAsync() => DeleteEntriesAsync(SelectedEntries.Select(entry => entry.StableId).ToArray());
    public async Task PasteAsync(string text, string? anchor)
    {
        if (_disposed) return;
        var pasted = await _authoring.PasteAsync(GroupId, text, anchor, ReviewAsync);
        if (_disposed) return;
        _copiedMove = _authoring.LastPasteCopiedMove;
        if (pasted.Count > 0) SetSelection(pasted);
        OnPropertyChanged(nameof(MoveStatus));
    }
    public async Task DuplicateSelectionAsync()
    {
        var text = CopySelection();
        if (text.Length > 0) await PasteAsync(text, SelectedEntry?.StableId);
    }

    [ObservableProperty]
    private decimal _batchAddCount = 1;

    public GroupEditorPanelViewModel(EditorWorkspaceViewModel workspace, string groupId, GroupEntryAuthoringService authoring, IProjectService projects, IAssetManager assetManager, EditorShortcutService shortcutService, IEffectCatalog effects, IEntryCatalog catalog, EntrySelectionContext selection, EditorRegistry? fieldEditors = null)
    {
        Workspace = workspace;
        GroupId = groupId;
        _entries = GroupNode.Entries;
        _authoring = authoring;
        _authoring.PendingMoveChanged += PendingMoveChanged;
        _projects = projects;
        _effects = effects;
        _catalog = catalog;
        _selection = selection;
        _localization = EditorLocalizationService.Current;
        if (_localization is not null) _localization.PropertyChanged += OnLocaleChanged;
        FieldEditors = fieldEditors ?? EditorFieldEditors.CreateDefault();
        AssetManager = assetManager;
        ShortcutService = shortcutService;
        EntryTypes = catalog.Definitions.Select(definition => new EntryTypeOption(definition.Type, definition.Category, $"Entry.Type.{definition.Type}")).ToArray();
        Workspace.VariableDefinitionsChanged += OnVariableDefinitionsChanged;
        _entries.CollectionChanged += OnEntriesChanged;
        foreach (var entry in _entries) Attach(entry);
    }

    [RelayCommand]
    private void AddEntry()
    {
        var selectedIndex = SelectedEntry is null ? -1 : GroupNode.Entries.IndexOf(SelectedEntry);
        InsertEntries(selectedIndex >= 0 ? selectedIndex + 1 : GroupNode.Entries.Count, BatchCount, SelectedEntry?.StableId);
    }

    [RelayCommand]
    private void AppendEntry()
    {
        InsertEntries(GroupNode.Entries.Count, BatchCount);
    }

    [RelayCommand]
    private void AppendEntries()
    {
        InsertEntries(GroupNode.Entries.Count, BatchCount);
    }

    private int BatchCount => (int)Math.Clamp(BatchAddCount, 1, 1000);
    public void InsertType(string type, string? anchorId)
    {
        if (_disposed) return;
        var count = _pendingInsertion?.Count ?? 1;
        if (_pendingInsertion.HasValue) anchorId = _pendingInsertion.Value.Anchor;
        _pendingInsertion = null;
        var index = anchorId is null ? GroupNode.Entries.Count : GroupNode.Entries.ToList().FindIndex(entry => entry.StableId == anchorId) + 1;
        if (anchorId is not null && index == 0) { ErrorText = EditorLocalizationService.Current?["Entry.AnchorMissing"] ?? "The insertion target was removed."; return; }
        InsertionType = type; InsertEntries(index, count);
    }
    public void ChangeType(EntryEditorItemViewModel entry, string type)
    {
        if (!_disposed && EntryTypes.Any(item => item.Value == type)) Workspace.ChangeEntryType(GroupNode, entry, type, _catalog.Create(type).Values);
    }
    public Task ChangeTypeAsync(EntryEditorItemViewModel entry, string type)
    {
        if (_disposed || entry.Type == type || !GroupNode.Entries.Contains(entry) || !EntryTypes.Any(item => item.Value == type)) return Task.CompletedTask;
        var fields = _projects.Current?.Services.GetService(typeof(EntryFieldAuthoringService)) as EntryFieldAuthoringService;
        if (fields is null) throw new InvalidOperationException("Entry authoring is unavailable.");
        return CommitReviewedAsync(fields.PrepareType(GroupId, entry.Source, type, GalNet.Editor.Abstraction.Documents.EditorEntryData.ParseParameters(_catalog, type, _catalog.Create(type).Values)));
    }
    private void InsertEntries(int index, int count, string? anchorId = null)
    {
        if (_disposed) return;
        ErrorText = "";
        if (!EntryTypes.Any(item => item.Value == InsertionType)) { _pendingInsertion = (anchorId, count); TypeSelectionRequested?.Invoke(); return; }
        var inserted = Workspace.InsertEntriesInto(GroupNode, index, count, InsertionType);
        if (inserted.Count > 0)
            SelectedEntry = inserted[^1];
    }

    [RelayCommand]
    private async Task RemoveEntryAsync(EntryEditorItemViewModel? entry)
    {
        if (entry is null)
            return;

        try { await DeleteEntriesAsync([entry.StableId]); }
        catch (Exception error) { ReportError(error); }
    }

    [RelayCommand]
    private void MoveEntryUp(EntryEditorItemViewModel? entry)
    {
        MoveEntry(entry, -1);
    }

    [RelayCommand]
    private void MoveEntryDown(EntryEditorItemViewModel? entry)
    {
        MoveEntry(entry, 1);
    }

    [RelayCommand]
    private void ReorderEntry(ReorderRequest? request)
    {
        if (request?.Item is not EntryEditorItemViewModel entry)
            return;

        var ids = SelectedEntries.Contains(entry) ? SelectedEntries.Select(item => item.StableId).ToArray() : [entry.StableId];
        var visible = VisibleEntries.ToArray();
        var index = string.IsNullOrWhiteSpace(SearchText) ? request.NewIndex
            : request.NewIndex >= visible.Length ? GroupNode.Entries.Count : GroupNode.Entries.IndexOf(visible[Math.Max(0, request.NewIndex)]);
        try { _authoring.Move(GroupId, ids, entry.StableId, index); }
        catch (Exception error) { ReportError(error); }
    }

    private void MoveEntry(EntryEditorItemViewModel? entry, int delta)
    {
        if (entry is null)
            return;

        ReorderEntry(new ReorderRequest(entry, GroupNode.Entries.IndexOf(entry) + delta));
    }

    private void OnVariableDefinitionsChanged()
    {
        OnPropertyChanged(nameof(ConditionSuggestions));
        OnPropertyChanged(nameof(ValidationVariables));
        foreach (var entry in GroupNode.Entries)
            Configure(entry);
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(VisibleEntries));
        if (e.Action == NotifyCollectionChangedAction.Move) return;
        foreach (var entry in _attached.Where(entry => !_entries.Contains(entry)).ToArray()) { entry.PropertyChanged -= OnEntryPropertyChanged; _attached.Remove(entry); }
        foreach (var entry in _entries.Where(entry => !_attached.Contains(entry)).ToArray()) Attach(entry);
        foreach (var entry in SelectedEntries.Where(entry => !_entries.Contains(entry)).ToArray()) SelectedEntries.Remove(entry);
        UpdateActiveSelection(null);
    }

    private void Attach(EntryEditorItemViewModel entry)
    {
        if (!_attached.Add(entry)) return;
        entry.IsPendingMove = _authoring.IsPendingMove(GroupId, entry.StableId);
        entry.IsParametersExpanded = _expanded.GetValueOrDefault(entry.StableId, true);
        entry.PropertyChanged += OnEntryPropertyChanged;
        Configure(entry);
    }

    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not EntryEditorItemViewModel entry) return;
        if (e.PropertyName == nameof(EntryEditorItemViewModel.IsParametersExpanded)) _expanded[entry.StableId] = entry.IsParametersExpanded;
        if (e.PropertyName == nameof(EntryEditorItemViewModel.Type)) Configure(entry);
    }

    private void Configure(EntryEditorItemViewModel entry) => entry.ConfigureParameterFields(
        _catalog,
        [],
        Workspace.AllProjectVariableDefinitions.Select(variable => variable.Name).ToArray(),
        _effects);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _authoring.PendingMoveChanged -= PendingMoveChanged;
        _selection.Clear(_selectionOwner);
        if (_localization is not null) _localization.PropertyChanged -= OnLocaleChanged;
        Workspace.VariableDefinitionsChanged -= OnVariableDefinitionsChanged;
        _entries.CollectionChanged -= OnEntriesChanged;
        foreach (var entry in _attached) { entry.PropertyChanged -= OnEntryPropertyChanged; entry.InvalidateParameterFields(); }
        _attached.Clear(); _expanded.Clear();
        GC.SuppressFinalize(this);
    }
}
