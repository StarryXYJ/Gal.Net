using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Editor.History;
using GalNet.Editor.Services;

namespace GalNet.Editor.ViewModels;

public sealed partial class CharactersPanelViewModel : ObservableObject, IUndoRedoTarget, IDisposable
{
    private readonly CharacterAuthoringService _characters;
    private readonly CharacterPanelState _state;
    private CharacterRow? _baseline;
    private bool _refreshing, _disposed, _new;
    private EditorText? _error;
    public IUndoRedoHistory UndoRedoHistory { get; }
    public ObservableCollection<CharacterRow> Rows { get; } = [];
    [ObservableProperty] private CharacterRow? _selected;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _draftName = "";
    [ObservableProperty] private string _draftKey = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private bool _busy;
    public bool CanEdit => !Busy && (_new || Selected is not null);
    public bool CanDelete => !Busy && Selected is { Uses: 0 } && _characters.Complete;
    public bool Incomplete => !_characters.Complete;
    public string IncompleteDetails => _characters.IncompleteDetails;
    public bool HasDraft => _new || _baseline is not null && (DraftName != _baseline.Name || DraftKey != _baseline.Id);
    public bool HasRows => Rows.Count > 0;
    public bool HasEditor => _new || Selected is not null;
    public string DeleteHintKey => CanDelete ? "Characters.Delete" : "Uiux.Panel.DeleteBlocked";
    public CharactersPanelViewModel(CharacterAuthoringService characters, EditorHistories histories, CharacterPanelState state)
    {
        _characters = characters; _state = state; UndoRedoHistory = histories.Content;
        characters.Changed += Refresh; characters.Text.PropertyChanged += OnLocale;
        Refresh();
    }
    partial void OnSearchChanged(string value) => Refresh();
    partial void OnBusyChanged(bool value) { OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanDelete)); OnPropertyChanged(nameof(DeleteHintKey)); }
    partial void OnDraftNameChanged(string value) => OnPropertyChanged(nameof(HasDraft));
    partial void OnDraftKeyChanged(string value) => OnPropertyChanged(nameof(HasDraft));
    partial void OnSelectedChanging(CharacterRow? value) { if (!_refreshing) SaveDraft(); }
    partial void OnSelectedChanged(CharacterRow? value)
    {
        if (!_refreshing)
        {
            _new = false;
            _state.SelectedNameKey = value?.DisplayNameKey; _state.SelectedId = value?.Id; Load();
        }
        OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanDelete)); OnPropertyChanged(nameof(HasEditor)); OnPropertyChanged(nameof(DeleteHintKey));
    }
    private void Load(bool clearError = true)
    {
        var draft = _new ? _state.NewDraft : Selected is { } selected ? _state.Drafts.GetValueOrDefault((selected.Id, selected.DisplayNameKey)) : null;
        _baseline = draft?.Baseline ?? Selected; DraftName = draft?.Name ?? _baseline?.Name ?? ""; DraftKey = draft?.Key ?? _baseline?.Id ?? "";
        if (clearError) _error = null;
        Render(); OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(HasEditor)); OnPropertyChanged(nameof(HasDraft));
    }
    private void SaveDraft()
    {
        if (_new) _state.NewDraft = new(null, DraftName, DraftKey);
        else if (_baseline is { } baseline)
        {
            var id = Selected?.Id ?? baseline.Id;
            if (HasDraft) _state.Drafts[(id, baseline.DisplayNameKey)] = new(baseline, DraftName, DraftKey);
            else _state.Drafts.Remove((id, baseline.DisplayNameKey));
        }
    }
    private void Refresh()
    {
        if (_disposed) return;
        SaveDraft();
        _refreshing = true;
        try
        {
            var all = _characters.Rows();
            _state.AmbiguousNameKeys.UnionWith(all.GroupBy(row => row.DisplayNameKey).Where(group => group.Count() > 1).Select(group => group.Key));
            foreach (var pair in _state.Drafts.ToArray())
            {
                if (_state.AmbiguousNameKeys.Contains(pair.Key.NameKey) || all.Any(row => row.Id == pair.Key.Id && row.DisplayNameKey == pair.Key.NameKey)) continue;
                var replacement = all.SingleOrDefault(row => row.DisplayNameKey == pair.Key.NameKey);
                if (replacement is not null && _state.Drafts.TryAdd((replacement.Id, replacement.DisplayNameKey), pair.Value)) _state.Drafts.Remove(pair.Key);
            }
            var selection = all.FirstOrDefault(row => row.Id == _state.SelectedId && row.DisplayNameKey == _state.SelectedNameKey);
            if (selection is null && _state.SelectedNameKey is { } nameKey && !_state.AmbiguousNameKeys.Contains(nameKey))
                selection = all.SingleOrDefault(row => row.DisplayNameKey == nameKey);
            if (selection is not null) _state.SelectedId = selection.Id;
            Rows.Clear();
            foreach (var row in all.Where(row => row.Id.Contains(Search, StringComparison.OrdinalIgnoreCase) || row.DisplayName.Contains(Search, StringComparison.OrdinalIgnoreCase))) Rows.Add(row);
            Selected = Rows.FirstOrDefault(row => row.Id == selection?.Id && row.DisplayNameKey == selection.DisplayNameKey);
            _new = _state.NewDraft is not null && _state.SelectedNameKey is null;
            Load(clearError: false);
        }
        finally { _refreshing = false; }
        OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanDelete)); OnPropertyChanged(nameof(Incomplete)); OnPropertyChanged(nameof(IncompleteDetails)); OnPropertyChanged(nameof(HasRows)); OnPropertyChanged(nameof(DeleteHintKey));
    }
    [RelayCommand]
    private void Add()
    {
        if (_disposed || Busy) return;
        SaveDraft(); _refreshing = true; Selected = null; _refreshing = false;
        _new = true; _baseline = null; _state.SelectedNameKey = null; _state.SelectedId = null;
        _state.NewDraft ??= new(null, "", ""); Load();
    }
    [RelayCommand]
    private async Task ApplyAsync() => await Run(async () =>
    {
        if (!_new && _baseline is null) return;
        var submittedNew = _new; var submittedBaseline = _baseline; var submittedName = DraftName; var submittedKey = DraftKey;
        var key = submittedKey;
        if (submittedNew) key = (await _characters.CreateAsync(submittedName, string.IsNullOrWhiteSpace(key) ? null : key)).Id;
        else if (submittedBaseline is not null) await _characters.UpdateAsync(submittedBaseline, key, submittedName);
        void ClearSubmitted((string Id, string NameKey) identity)
        {
            if (_state.Drafts.TryGetValue(identity, out var draft) && draft.Baseline == submittedBaseline && draft.Name == submittedName && draft.Key == submittedKey)
                _state.Drafts.Remove(identity);
        }
        if (submittedBaseline is { } baseline)
        { ClearSubmitted((baseline.Id, baseline.DisplayNameKey)); ClearSubmitted((key, baseline.DisplayNameKey)); }
        if (submittedNew && _state.NewDraft is { } fresh && fresh.Name == submittedName && fresh.Key == submittedKey) _state.NewDraft = null;
        if (_disposed) return;
        var stillEditingSubmitted = submittedNew ? _new : !_new && submittedBaseline is { } submitted
            && _state.SelectedNameKey == submitted.DisplayNameKey && (_state.SelectedId == submitted.Id || _state.SelectedId == key);
        if (!stillEditingSubmitted) { Refresh(); return; }
        _new = false; _baseline = null; Search = "";
        _state.SelectedNameKey = _characters.Rows().FirstOrDefault(row => row.Id == key)?.DisplayNameKey;
        _state.SelectedId = key;
        Refresh(); Load();
    });
    [RelayCommand] private async Task DeleteAsync() => await Run(async () => { if (Selected is { } row) await _characters.DeleteAsync(row); });
    [RelayCommand]
    private void Revert()
    {
        if (_baseline is { } baseline) _state.Drafts.Remove((Selected?.Id ?? baseline.Id, baseline.DisplayNameKey));
        if (_new) _state.NewDraft = null;
        _new = false; Load();
    }
    private async Task Run(Func<Task> action)
    {
        if (_disposed || Busy) return;
        Busy = true; _error = null;
        try { await action(); }
        catch (Exception error) { _error = EditorText.Error(error); }
        finally { Busy = false; if (!_disposed) { Refresh(); Render(); } }
    }
    private void OnLocale(object? sender, PropertyChangedEventArgs args)
    {
        if (_disposed) return;
        Render(); OnPropertyChanged(nameof(IncompleteDetails));
    }
    private void Render() => Message = _error?.Render(_characters.Text) ?? "";
    public void Dispose()
    {
        if (_disposed) return; SaveDraft(); _disposed = true;
        _characters.Changed -= Refresh; _characters.Text.PropertyChanged -= OnLocale;
    }
}
