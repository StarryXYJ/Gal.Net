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
    private CharacterRow? _baseline;
    private bool _refreshing, _disposed, _new;
    private EditorText? _error;
    public IUndoRedoHistory UndoRedoHistory { get; }
    public ObservableCollection<CharacterRow> Rows { get; } = [];
    public ObservableCollection<LegacyCharacterCandidate> LegacyRows { get; } = [];
    [ObservableProperty] private CharacterRow? _selected;
    [ObservableProperty] private LegacyCharacterCandidate? _selectedLegacy;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _draftName = "";
    [ObservableProperty] private string _draftKey = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private bool _busy;
    public bool CanEdit => !Busy && (_new || Selected is not null);
    public bool CanDelete => !Busy && Selected is { Uses: 0 } && _characters.Complete;
    public bool HasLegacy => LegacyRows.Count > 0;
    public bool Incomplete => !_characters.Complete;
    private bool HasDraft => _new || _baseline is not null && (DraftName != _baseline.Name || DraftKey != _baseline.Id);
    public CharactersPanelViewModel(CharacterAuthoringService characters, EditorHistories histories)
    {
        _characters = characters; UndoRedoHistory = histories.Content;
        characters.Changed += Refresh; characters.Text.PropertyChanged += OnLocale;
        Refresh();
    }
    partial void OnSearchChanged(string value) => Refresh();
    partial void OnBusyChanged(bool value) { OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanDelete)); }
    partial void OnSelectedChanged(CharacterRow? value)
    {
        if (!_refreshing) { _new = false; Load(); }
        OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanDelete));
    }
    private void Load()
    {
        _baseline = Selected; DraftName = _baseline?.Name ?? ""; DraftKey = _baseline?.Id ?? "";
        _error = null; Render(); OnPropertyChanged(nameof(CanEdit));
    }
    private void Refresh()
    {
        if (_disposed) return;
        var preserve = HasDraft; var id = Selected?.Id;
        _refreshing = true;
        try
        {
            Rows.Clear();
            foreach (var row in _characters.Rows().Where(row => row.Id.Contains(Search, StringComparison.OrdinalIgnoreCase) || row.DisplayName.Contains(Search, StringComparison.OrdinalIgnoreCase))) Rows.Add(row);
            Selected = Rows.FirstOrDefault(row => row.Id == id);
            if (!preserve) Load();
            var legacy = SelectedLegacy?.Value; LegacyRows.Clear();
            foreach (var row in _characters.LegacyCandidates()) LegacyRows.Add(row);
            SelectedLegacy = LegacyRows.FirstOrDefault(row => row.Value == legacy);
        }
        finally { _refreshing = false; }
        OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanDelete)); OnPropertyChanged(nameof(HasLegacy)); OnPropertyChanged(nameof(Incomplete));
    }
    [RelayCommand]
    private void Add()
    {
        Selected = null; _new = true; _baseline = null; DraftName = ""; DraftKey = "";
        _error = null; Render(); OnPropertyChanged(nameof(CanEdit));
    }
    [RelayCommand]
    private async Task ApplyAsync() => await Run(async () =>
    {
        if (!_new && _baseline is null) return;
        var key = DraftKey;
        if (_new) key = (await _characters.CreateAsync(DraftName, string.IsNullOrWhiteSpace(key) ? null : key)).Id;
        else if (_baseline is not null) await _characters.UpdateAsync(_baseline, key, DraftName);
        if (_disposed) return;
        _new = false; _baseline = null; Refresh(); Selected = Rows.FirstOrDefault(row => row.Id == key); Load();
    });
    [RelayCommand] private async Task DeleteAsync() => await Run(async () => { if (Selected is { } row) await _characters.DeleteAsync(row); });
    [RelayCommand] private void Revert() { _new = false; Load(); }
    [RelayCommand] private async Task ImportLegacyAsync() => await Run(async () => { if (SelectedLegacy is { } legacy) await _characters.ImportLegacyAsync(legacy.Value); });
    private async Task Run(Func<Task> action)
    {
        if (_disposed || Busy) return;
        Busy = true; _error = null;
        try { await action(); }
        catch (Exception error) { _error = EditorText.Error(error); }
        finally { Busy = false; if (!_disposed) { Refresh(); Render(); } }
    }
    private void OnLocale(object? sender, PropertyChangedEventArgs args) { if (!_disposed) Render(); }
    private void Render() => Message = _error?.Render(_characters.Text) ?? "";
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _characters.Changed -= Refresh; _characters.Text.PropertyChanged -= OnLocale;
    }
}
