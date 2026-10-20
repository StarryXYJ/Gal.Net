using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Editor.History;
using GalNet.Editor.Services;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.ViewModels;

public sealed record LocalizationGridRow(LocalizationTableRow Source, IReadOnlyList<LocalizationGridCell> Cells)
{
    public string Context => Source.Context;
}

public sealed partial class LocalizationGridCell : ObservableObject
{
    private readonly GameLocalizationPanelViewModel _panel;
    private LocalizationCellDraft? _draft;
    private bool _loading;
    private string? _value;
    private bool _pending;
    private string _sourceLocale = "";
    private string? _sourceText;
    private string? _baselineValue;
    private string _baselineLocale = "";
    private string? _baselineSource;
    public string Category { get; }
    public string Key { get; }
    public string Culture { get; }
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private bool _busy;
    public bool IsDirty => _pending;
    public bool Enabled => !_panel.IsDisposed;
    public LocalizationGridCell(GameLocalizationPanelViewModel panel, string category, string key, string culture)
    { _panel = panel; Category = category; Key = key; Culture = culture; }
    partial void OnBusyChanged(bool value) => OnPropertyChanged(nameof(Enabled));
    partial void OnTextChanged(string value)
    {
        if (_loading) return;
        if (!IsDirty)
        {
            _pending = true; _baselineValue = _value; _baselineLocale = _sourceLocale; _baselineSource = _sourceText;
            try { _draft = _panel.Service.Capture(Category, Key, Culture); }
            catch (Exception error) { Message = EditorText.Error(error).Render(_panel.Service.Text); return; }
            _panel.DraftChanged();
        }
        if (_baselineValue is not null && value == _baselineValue) { _pending = false; _draft = null; Message = ""; _panel.DraftChanged(); }
    }
    public void Refresh(LocalizationTableSnapshot snapshot, JsonObject? revision)
    {
        _value = snapshot.Index.FindDirect(Culture, Key)?.Text;
        _sourceLocale = snapshot.Source; _sourceText = snapshot.Index.FindDirect(snapshot.Source, Key)?.Text;
        var state = _value is null ? "Missing" : _value.Length == 0 ? "Empty" : "";
        var review = _value is null || Culture == snapshot.Source ? "" : LocalizationTextState.NeedsReview(revision, Culture, snapshot.Source)
            switch
        { true => "Review", false => "", null => "Unknown" };
        Status = string.Join(" · ", new[] { state, review }.Where(value => value.Length > 0).Select(value => _panel.Service.Text["I18n." + value]));
        if (!IsDirty)
        {
            _loading = true;
            try { Text = _value ?? ""; } finally { _loading = false; }
        }
    }
    public async Task CommitAsync(bool explicitValue = false)
    {
        if (Busy || _panel.IsDisposed || !IsDirty && !explicitValue) return;
        Busy = true; Message = "";
        try
        {
            if (!IsDirty) { _pending = true; _baselineValue = _value; _baselineLocale = _sourceLocale; _baselineSource = _sourceText; }
            _draft ??= _panel.Service.Capture(Category, Key, Culture);
            if (_draft.Target.Text != _baselineValue || _draft.SourceLocale != _baselineLocale || _draft.SourceText != _baselineSource)
                throw new GalNet.Editor.Abstraction.Services.EditorOperationException("i18n.stale", _panel.Service.Text["I18n.Stale"]);
            await _panel.Service.SetAsync(_draft, Text);
            _pending = false; _draft = null;
        }
        catch (Exception error) { Message = EditorText.Error(error).Render(_panel.Service.Text); throw; }
        finally { Busy = false; _panel.Refresh(); }
    }
    public void Revert() { _pending = false; _draft = null; Message = ""; _panel.Refresh(); _panel.DraftChanged(); }
}

/// <summary>Project-scoped to retain cell drafts when the dock view closes or categories change.</summary>
public sealed partial class GameLocalizationPanelViewModel : ObservableObject, IUndoRedoTarget, IDisposable
{
    public GameLocalizationPanelService Service { get; }
    public IUndoRedoHistory UndoRedoHistory { get; }
    public ObservableCollection<LocalizationCategory> Categories { get; } = [];
    public ObservableCollection<string> Languages { get; } = [];
    public ObservableCollection<LocalizationGridRow> Rows { get; } = [];
    private readonly Dictionary<(string Category, string Key, string Culture), LocalizationGridCell> _cells = [];
    private readonly IDisposable _draftRegistration;
    private bool _refreshing;
    public bool IsDisposed { get; private set; }
    public event Action? StructureChanged;
    [ObservableProperty] private LocalizationCategory? _selected;
    [ObservableProperty] private string _newLanguage = "";
    [ObservableProperty] private string? _developmentLanguage;
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private string _previewMessage = "";
    [ObservableProperty] private bool _incomplete;
    private DevelopmentLanguagePreview? _preview;
    public bool CanChangeDevelopmentLanguage => _preview is not null;
    public GameLocalizationPanelViewModel(GameLocalizationPanelService service, EditorHistories histories)
    {
        Service = service; UndoRedoHistory = histories.Content;
        _draftRegistration = service.Content.RegisterDraft(() => _cells.Values.Any(cell => cell.IsDirty), CommitDraftsAsync);
        service.Content.Changed += ContentChanged; service.Text.PropertyChanged += LocaleChanged;
        Refresh();
    }
    partial void OnSelectedChanged(LocalizationCategory? value) { if (!_refreshing) Refresh(); }
    partial void OnDevelopmentLanguageChanged(string? value) { _preview = null; PreviewMessage = ""; OnPropertyChanged(nameof(CanChangeDevelopmentLanguage)); }
    private void ContentChanged()
    { if (Dispatcher.UIThread.CheckAccess()) Refresh(); else Dispatcher.UIThread.Post(Refresh); }
    private void LocaleChanged(object? sender, PropertyChangedEventArgs args)
    {
        void Update() { Refresh(); if (!IsDisposed) StructureChanged?.Invoke(); }
        if (Dispatcher.UIThread.CheckAccess()) Update(); else Dispatcher.UIThread.Post(Update);
    }
    public void DraftChanged() { if (!_refreshing && !IsDisposed) Service.Content.Refresh(); }
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
            if (changed) { Languages.Clear(); foreach (var language in snapshot.Languages) Languages.Add(language); }
            var states = JsonNode.Parse(LocalizationJson.Decode(snapshot.Metadata))?["textStates"];
            var rows = snapshot.Rows.Select(row => new LocalizationGridRow(row, snapshot.Languages.Select(culture =>
            {
                var identity = (Selected!.Id, row.Key, culture);
                if (!_cells.TryGetValue(identity, out var cell)) _cells.Add(identity, cell = new(this, identity.Id, row.Key, culture));
                cell.Refresh(snapshot, states?[row.Key] as JsonObject); return cell;
            }).ToArray())).ToArray();
            if (changed || Rows.Count != rows.Length || !Rows.Zip(rows).All(pair => pair.First.Context == pair.Second.Context && pair.First.Cells.SequenceEqual(pair.Second.Cells)))
            { Rows.Clear(); foreach (var row in rows) Rows.Add(row); }
            if (changed) StructureChanged?.Invoke();
        }
        catch (Exception error) { Message = EditorText.Error(error).Render(Service.Text); }
        finally { _refreshing = false; }
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
    [RelayCommand] private Task AddLanguageAsync() => RunAsync(async () => { await Service.AddLanguageAsync(NewLanguage); NewLanguage = ""; });
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
    [RelayCommand] private void RevertDrafts() { foreach (var cell in _cells.Values.ToArray()) if (cell.IsDirty) cell.Revert(); }
    public void Dispose()
    {
        if (IsDisposed) return; IsDisposed = true; _draftRegistration.Dispose();
        Service.Content.Changed -= ContentChanged; Service.Text.PropertyChanged -= LocaleChanged;
    }
}
