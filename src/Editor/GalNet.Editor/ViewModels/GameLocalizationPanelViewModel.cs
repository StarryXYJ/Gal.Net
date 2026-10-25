using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using GalNet.Editor.Services;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.ViewModels;

public sealed class LocalizationGridRow(LocalizationTableRow source, IReadOnlyList<LocalizationGridCell> cells) : ObservableObject
{
    public LocalizationTableRow Source { get; private set; } = source;
    public IReadOnlyList<LocalizationGridCell> Cells { get; } = cells;
    public string Context => Source.Context;
    public string Title => Source.Locations.FirstOrDefault()?.Title ?? "";
    public string Slot => Source.Locations.FirstOrDefault()?.Slot ?? "";
    public string Details => string.Join("\n", Source.Locations.Select(location => location.Details));
    public string ReferenceCount => Source.References.Count > 1 ? "×" + Source.References.Count : "";
    public void Update(LocalizationTableRow value)
    {
        Source = value;
        OnPropertyChanged(nameof(Title)); OnPropertyChanged(nameof(Slot));
        OnPropertyChanged(nameof(Details)); OnPropertyChanged(nameof(ReferenceCount));
    }
}

public sealed partial class LocalizationImportRowViewModel : ObservableObject
{
    public LocalizationImportCandidate Candidate { get; }
    public string Source { get; }
    public string Translation => Candidate.Row.Translation ?? "";
    public string ExportedSource => Candidate.Row.SourceText ?? "";
    public string Status { get; }
    public bool CanInclude => Candidate.Known && Candidate.Row.Translation is not null;
    [ObservableProperty] private bool _include;
    public LocalizationImportRowViewModel(LocalizationImportCandidate candidate, IEditorLocalizationService text)
    {
        Candidate = candidate; Source = candidate.CurrentSource ?? candidate.Row.SourceText ?? text["I18n.Missing"];
        Status = text[!candidate.Known ? "Exchange.Unknown" : candidate.Row.Translation is null ? "Exchange.Skip" : candidate.Row.Translation.Length == 0 ? "Exchange.Clear" : candidate.SourceChanged ? "Uiux.I18n.SourceChanged" : candidate.Target?.Text == candidate.Row.Translation ? "Exchange.Unchanged" : "Exchange.Change"];
        Include = CanInclude && candidate.Row.Translation!.Length > 0 && (candidate.SourceChanged || candidate.Target?.Text != candidate.Row.Translation);
    }
}

public sealed partial class LocalizationGridCell : ObservableObject
{
    private readonly GameLocalizationPanelViewModel _panel;
    private LocalizationCellDraft? _target;
    private bool _loading;
    private string? _value;
    private bool _pending;
    private Task? _write;
    private Exception? _writeError;
    private string _submittingText = "";
    public string Category { get; }
    public string Key { get; }
    public string Culture { get; }
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private string _message = "";
    public bool HasMessage => Message.Length > 0;
    partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(HasMessage));
    [ObservableProperty] private bool _busy;
    public bool IsDirty => _pending;
    public bool IsMissing { get; private set; }
    public string DisplayText => IsMissing && Text.Length == 0 ? _panel.Service.Text["I18n.Missing"] : Text;
    public double Width => _panel.ColumnWidth(Culture);
    public void WidthChanged() => OnPropertyChanged(nameof(Width));
    public bool Enabled => !_panel.IsDisposed && _panel.Languages.Contains(Culture);
    public LocalizationGridCell(GameLocalizationPanelViewModel panel, string category, string key, string culture)
    { _panel = panel; Category = category; Key = key; Culture = culture; }
    partial void OnTextChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayText));
        if (_loading) return;
        if (!_pending)
        {
            if (_value == value) return;
            _pending = true;
            try { _target = _panel.Service.Capture(Category, Key, Culture); }
            catch (Exception error) { SetError(error); _panel.DraftChanged(); return; }
            _panel.DraftChanged();
        }
        else if (!Busy && value == _value)
        {
            _pending = false; _target = null; _writeError = null; Message = "";
            _panel.DraftChanged();
        }
    }
    public void Refresh(LocalizationTableSnapshot snapshot)
    {
        _value = snapshot.Index.FindDirect(Culture, Key)?.Text;
        IsMissing = _value is null;
        OnPropertyChanged(nameof(DisplayText)); OnPropertyChanged(nameof(IsMissing)); OnPropertyChanged(nameof(Enabled));
        if (!IsDirty)
        {
            _loading = true;
            try { Text = _value ?? ""; } finally { _loading = false; }
        }
    }
    private void SetError(Exception error)
    { _writeError = error; Message = EditorText.Error(error).Render(_panel.Service.Text); }
    private async Task WriteAsync()
    {
        var value = Text; var captured = _target;
        _submittingText = value;
        Busy = true; Message = ""; _writeError = null;
        // Publish the task before beginning work: Save All and a second input can join it.
        await Task.Yield();
        try
        {
            if (_pending && !_panel.IsDisposed)
            {
                var target = captured ?? throw new EditorOperationException("i18n.stale", _panel.Service.Text["I18n.Stale"]);
                await _panel.WriteCellAsync(target, value);
                _value = value;
                if (Text == value) { _pending = false; _target = null; }
                else
                {
                    var next = _panel.Service.Capture(Category, Key, Culture);
                    // Source editing advances its own baseline. A translation must keep its original source.
                    if (Culture != target.SourceLocale && (next.SourceLocale != target.SourceLocale || next.SourceText != target.SourceText))
                        throw new EditorOperationException("i18n.stale", _panel.Service.Text["I18n.Stale"]);
                    _target = next;
                }
            }
        }
        catch (Exception error) { SetError(error); }
        finally { Busy = false; _panel.Refresh(); _panel.DraftChanged(); }
    }
    public async Task CommitAsync(bool explicitValue = false)
    {
        if (_panel.IsDisposed) return;
        if (Busy && _write is { } inFlight) await inFlight;
        if (_panel.IsDisposed) return;
        if (!Busy && explicitValue && !_pending && IsMissing)
        { _pending = true; _target = _panel.Service.Capture(Category, Key, Culture); }
        if (!Busy && _pending) _write = WriteAsync();
        if (_write is { } write) await write;
        if (_writeError is { } error) throw error;
    }
    // Uncommitted input can be discarded; successful updates use project undo.
    public void Revert()
    {
        if (Busy)
        {
            _loading = true;
            try { Text = _submittingText; }
            finally { _loading = false; }
            return;
        }
        _pending = false; _target = null; _writeError = null; Message = "";
        _panel.Refresh(); _panel.DraftChanged();
    }
}

/// <summary>Project-scoped resource table and view preferences; text commits on blur or explicit save.</summary>
public sealed partial class GameLocalizationPanelViewModel : ObservableObject, IUndoRedoTarget, IDisposable
{
    public GameLocalizationPanelService Service { get; }
    public IUndoRedoHistory UndoRedoHistory { get; }
    public ObservableCollection<LocalizationCategory> Categories { get; } = [];
    public ObservableCollection<string> Languages { get; } = [];
    public ObservableCollection<LocalizationGridRow> Rows { get; } = [];
    private LocalizationGridRow[] _allRows = [];
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private bool _onlyMissing;
    private readonly HashSet<string> _hiddenLanguages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> _columnWidths = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _writes = new(1, 1);
    public IReadOnlyList<string> VisibleLanguages => Languages.Where(IsLanguageVisible).ToArray();
    public bool IsLanguageVisible(string code) => code == Languages.FirstOrDefault() || !_hiddenLanguages.Contains(code);
    public double ColumnWidth(string code) => _columnWidths.GetValueOrDefault(code, 244);
    public void SetColumnWidth(string code, double width)
    {
        _columnWidths[code] = Math.Clamp(width, 140, 720);
        foreach (var cell in _cells.Values.Where(cell => cell.Culture == code)) cell.WidthChanged();
    }
    public void SetLanguageVisible(string code, bool visible)
    {
        if (code == Languages.FirstOrDefault()) return;
        if (visible) _hiddenLanguages.Remove(code); else _hiddenLanguages.Add(code);
        Refresh(); StructureChanged?.Invoke();
    }
    public async Task WriteCellAsync(LocalizationCellDraft target, string value)
    {
        await _writes.WaitAsync();
        try { if (!IsDisposed) await Service.SetAsync(target, value); }
        finally { _writes.Release(); }
    }
    public IReadOnlyList<string> LanguageSuggestions { get; private set; } = [];
    partial void OnNewLanguageChanged(string value) => UpdateLanguageSuggestions();
    private void UpdateLanguageSuggestions()
    {
        LanguageSuggestions = CultureInfo.GetCultures(CultureTypes.NeutralCultures | CultureTypes.SpecificCultures)
            .Where(culture => culture.Name.Length > 0 && !Languages.Contains(culture.Name, StringComparer.OrdinalIgnoreCase))
            .Where(culture => string.IsNullOrWhiteSpace(NewLanguage) || new[] { culture.Name, culture.NativeName, culture.EnglishName }
                .Any(name => name.Contains(NewLanguage.Trim().Replace('_', '-'), StringComparison.OrdinalIgnoreCase)))
            .OrderBy(culture => culture.Name is "en-US" or "zh-CN" or "ja-JP" or "ko-KR" ? 0 : 1)
            .ThenBy(culture => culture.Name, StringComparer.OrdinalIgnoreCase)
            .Select(culture => culture.Name + " — " + culture.NativeName).ToArray();
        OnPropertyChanged(nameof(LanguageSuggestions));
    }
    public bool HasRows => Rows.Count > 0;
    partial void OnSearchChanged(string value) => ProjectRows();
    partial void OnOnlyMissingChanged(bool value) => ProjectRows();
    private readonly Dictionary<(string Category, string Key, string Culture), LocalizationGridCell> _cells = [];
    private readonly IDisposable _draftRegistration;
    private bool _refreshing;
    private bool _notifyingDraft;
    public bool IsDisposed { get; private set; }
    public event Action? StructureChanged;
    private readonly LocalizationExchangeService _exchange;
    private readonly IFileDialogService _dialogs;
    private LocalizationImportPreview? _import;
    public ObservableCollection<LocalizationImportRowViewModel> ImportRows { get; } = [];
    [ObservableProperty] private string? _exchangeLanguage;
    [ObservableProperty] private bool _exportAll = true;
    [ObservableProperty] private bool _exportRuntime;
    [ObservableProperty] private bool _allowEmpty;
    public bool HasImport => _import is not null;
    partial void OnExchangeLanguageChanged(string? value) => CancelImport();
    partial void OnAllowEmptyChanged(bool value) { if (!value) foreach (var row in ImportRows.Where(row => row.Candidate.Row.Translation?.Length == 0)) row.Include = false; }
    [ObservableProperty] private LocalizationCategory? _selected;
    [ObservableProperty] private string _newLanguage = "";
    [ObservableProperty] private string? _developmentLanguage;
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private string _previewMessage = "";
    [ObservableProperty] private bool _incomplete;
    private DevelopmentLanguagePreview? _preview;
    public bool CanChangeDevelopmentLanguage => _preview is not null;
    public GameLocalizationPanelViewModel(GameLocalizationPanelService service, EditorHistories histories, LocalizationExchangeService exchange, IFileDialogService dialogs)
    {
        _exchange = exchange; _dialogs = dialogs;
        Service = service; UndoRedoHistory = histories.Content;
        _draftRegistration = service.Content.RegisterDraft(() => _cells.Values.Any(cell => cell.IsDirty), CommitDraftsAsync,
            () => { foreach (var cell in _cells.Values.Where(cell => cell.IsDirty).ToArray()) cell.Revert(); });
        service.Content.Changed += ContentChanged; service.Text.PropertyChanged += LocaleChanged;
        Refresh();
    }
    [RelayCommand]
    private Task ExportAsync() => RunAsync(async () =>
    {
        var culture = ExchangeLanguage ?? Languages.FirstOrDefault(language => language != Languages[0]) ?? Languages[0];
        var path = await _dialogs.SaveFilePickerAsync(Service.Text["Exchange.Export"], culture + (ExportRuntime ? ".texts.json" : ".template.json"));
        if (path is null || IsDisposed) return;
        await _exchange.ExportFileAsync(path, culture, ExportAll ? null : Selected?.Id, ExportRuntime);
    });
    [RelayCommand]
    private Task PreviewImportAsync() => RunAsync(async () =>
    {
        var files = await _dialogs.OpenFilePickerAsync(Service.Text["Exchange.Import"], false);
        if (files.Count == 0 || IsDisposed) return;
        CancelImport();
        var info = new FileInfo(files[0]); if (info.Length > 8 * 1024 * 1024) throw new InvalidDataException("Exchange file exceeds 8 MiB.");
        var bytes = await File.ReadAllBytesAsync(files[0]); if (IsDisposed) return;
        var json = new System.Text.UTF8Encoding(false, true).GetString(bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? bytes.AsSpan(3) : bytes);
        var preview = _exchange.Preview(json, ExchangeLanguage ?? Languages.FirstOrDefault(language => language != Languages[0]) ?? Languages[0]);
        ExchangeLanguage = preview.Culture; _import = preview;
        ImportRows.Clear(); foreach (var row in _import.Rows) ImportRows.Add(new(row, Service.Text));
        AllowEmpty = false; OnPropertyChanged(nameof(HasImport));
    });
    [RelayCommand]
    private Task ApplyImportAsync() => RunAsync(async () =>
    {
        if (_import is not { } preview) return;
        await _exchange.ApplyAsync(preview, ImportRows.Where(row => row.Include).Select(row => row.Candidate.Row.Key).ToArray(), AllowEmpty);
        CancelImport();
    });
    [RelayCommand] private void CancelImport() { _import = null; ImportRows.Clear(); OnPropertyChanged(nameof(HasImport)); }
    partial void OnSelectedChanged(LocalizationCategory? value) { if (!_refreshing) Refresh(); }
    partial void OnDevelopmentLanguageChanged(string? value) { _preview = null; PreviewMessage = ""; OnPropertyChanged(nameof(CanChangeDevelopmentLanguage)); }
    private void ContentChanged()
    { if (_notifyingDraft) return; if (Dispatcher.UIThread.CheckAccess()) Refresh(); else Dispatcher.UIThread.Post(Refresh); }
    private void LocaleChanged(object? sender, PropertyChangedEventArgs args)
    {
        void Update() { Refresh(); if (!IsDisposed) StructureChanged?.Invoke(); }
        if (Dispatcher.UIThread.CheckAccess()) Update(); else Dispatcher.UIThread.Post(Update);
    }
    public void DraftChanged()
    {
        if (_refreshing || IsDisposed) return;
        // Notify project dirty/save state without rebuilding this table for local input.
        _notifyingDraft = true;
        try { Service.Content.Refresh(); }
        finally { _notifyingDraft = false; }
    }
    public void Refresh()
    {
        if (_refreshing || IsDisposed) return;
        _refreshing = true;
        try
        {
            var category = Selected?.Id ?? "characters"; var snapshot = Service.Snapshot(category);
            Incomplete = !snapshot.Index.Complete;
            if (!Categories.SequenceEqual(snapshot.Categories))
            {
                Categories.Clear(); foreach (var item in snapshot.Categories) Categories.Add(item);
                Selected = Categories.FirstOrDefault(item => item.Id == category) ?? Categories.First();
            }
            if (Selected?.Id != category) snapshot = Service.Snapshot(Selected!.Id);
            var changed = !Languages.SequenceEqual(snapshot.Languages);
            if (changed)
            {
                Languages.Clear(); foreach (var language in snapshot.Languages) Languages.Add(language);
                CancelImport(); DevelopmentLanguage = null; UpdateLanguageSuggestions();
            }
            if (ExchangeLanguage is null || !Languages.Contains(ExchangeLanguage)) ExchangeLanguage = Languages.FirstOrDefault(language => language != snapshot.Source) ?? snapshot.Source;
            var rows = snapshot.Rows.Select(row => new LocalizationGridRow(row, VisibleLanguages.Select(culture =>
            {
                var identity = (Selected!.Id, row.Key, culture);
                if (!_cells.TryGetValue(identity, out var cell)) _cells.Add(identity, cell = new(this, identity.Id, row.Key, culture));
                cell.Refresh(snapshot); return cell;
            }).ToArray())).ToArray();
            var previousRows = _allRows.ToDictionary(row => row.Source.Key, StringComparer.Ordinal);
            _allRows = rows.Select(row =>
            {
                if (!previousRows.TryGetValue(row.Source.Key, out var existing) || !existing.Cells.SequenceEqual(row.Cells)) return row;
                existing.Update(row.Source); return existing;
            }).ToArray(); ProjectRows();
            if (changed) StructureChanged?.Invoke();
        }
        catch (Exception error) { Message = EditorText.Error(error).Render(Service.Text); }
        finally { _refreshing = false; }
    }
    private void ProjectRows()
    {
        var rows = _allRows.Where(row => (string.IsNullOrWhiteSpace(Search) || row.Context.Contains(Search, StringComparison.OrdinalIgnoreCase) || row.Cells.Any(cell => cell.Text.Contains(Search, StringComparison.OrdinalIgnoreCase)))
            && (!OnlyMissing || row.Cells.Any(cell => cell.Culture != Languages[0] && cell.IsMissing))).ToArray();
        if (Rows.Count != rows.Length || !Rows.Zip(rows).All(pair => pair.First.Context == pair.Second.Context && pair.First.Cells.SequenceEqual(pair.Second.Cells)))
        { Rows.Clear(); foreach (var row in rows) Rows.Add(row); }
        OnPropertyChanged(nameof(HasRows));
    }
    public async Task CommitDraftsAsync()
    {
        foreach (var cell in _cells.Values.Where(cell => cell.IsDirty).ToArray()) await cell.CommitAsync();
    }
    public async Task RunAsync(Func<Task> action)
    {
        if (IsDisposed) return;
        try { Message = ""; await action(); }
        catch (Exception error) { Message = EditorText.Error(error).Render(Service.Text); }
        finally { Refresh(); }
    }
    [RelayCommand]
    private Task AddLanguageAsync() => RunAsync(async () =>
    {
        var code = NewLanguage.Split(" — ", 2, StringSplitOptions.None)[0];
        await Service.AddLanguageAsync(code); _hiddenLanguages.Remove(code); NewLanguage = "";
    });
    public Task RemoveLanguageAsync(string code) => RunAsync(async () =>
    {
        if (ApplyImportCommand.IsRunning) throw new EditorOperationException("i18n.stale", Service.Text["I18n.Stale"]);
        foreach (var cell in _cells.Values.Where(cell => cell.Culture == code).ToArray()) await cell.CommitAsync();
        CancelImport(); await Service.RemoveLanguageAsync(code);
    });
    [RelayCommand]
    private void PreviewDevelopmentLanguage()
    {
        try
        {
            _preview = Service.Preview(DevelopmentLanguage ?? "");
            PreviewMessage = Service.Text.Format("I18n.Preview", _preview.Target, _preview.Missing);
            Message = "";
        }
        catch (Exception error) { _preview = null; Message = EditorText.Error(error).Render(Service.Text); }
        OnPropertyChanged(nameof(CanChangeDevelopmentLanguage));
    }
    [RelayCommand]
    private Task ChangeDevelopmentLanguageAsync() => RunAsync(async () =>
    {
        if (_preview is not { } preview) return;
        await Service.ChangeDevelopmentLanguageAsync(preview); _preview = null; PreviewMessage = "";
        OnPropertyChanged(nameof(CanChangeDevelopmentLanguage));
    });
    public void Dispose()
    {
        if (IsDisposed) return; IsDisposed = true; _draftRegistration.Dispose();
        Service.Content.Changed -= ContentChanged; Service.Text.PropertyChanged -= LocaleChanged;
    }
}
