using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Core.Configuration;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using GalNet.Editor.Services;
using GalNet.Editor.Services.Interfaces;
using GalNet.Editor.Shared.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Editor.ViewModels;

public sealed class ResourcePatchColumn : ObservableObject
{
    private readonly Action _changed;
    private string _name, _priority;
    public ResourceOverrideDefinition Definition { get; private set; }
    public IReadOnlyDictionary<string, string> Mappings { get; private set; }
    public string Id => Definition.Id;
    public string Name { get => _name; set { if (SetProperty(ref _name, value)) _changed(); } }
    public string Priority { get => _priority; set { if (SetProperty(ref _priority, value)) _changed(); } }
    public bool HasDraft => Name != Definition.Name || Priority != Definition.Priority.ToString(CultureInfo.InvariantCulture);
    public ResourcePatchColumn(ResourceOverrideDefinition definition, Action changed)
    { Definition = definition; Mappings = definition.Mappings; _name = definition.Name; _priority = definition.Priority.ToString(CultureInfo.InvariantCulture); _changed = changed; }
    public void Refresh(ResourceOverrideDefinition definition)
    {
        Mappings = definition.Mappings;
        if (HasDraft) return;
        Accept(definition);
    }
    public void Accept(ResourceOverrideDefinition definition)
    {
        Mappings = definition.Mappings;
        Definition = definition; _name = definition.Name; _priority = definition.Priority.ToString(CultureInfo.InvariantCulture);
        OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(Priority));
    }
}

public sealed partial class ResourceOverridesPanelViewModel : ObservableObject, IUndoRedoTarget, IDisposable
{
    private readonly IProjectService _projects;
    private readonly IFileDialogService _dialogs;
    private readonly IDisposable _draft;
    private readonly IEditorAssetChanges? _assetChanges;
    private CancellationTokenSource? _refresh, _export;
    private Task? _refreshTask;
    private bool _disposed, _notifyingDraft;
    private long _revision;
    private string _analysisMessage = "";
    private string _structureKey = "";
    public ResourceOverrideAuthoringService Service { get; }
    public IEditorLocalizationService L { get; }
    public IUndoRedoHistory UndoRedoHistory => Service.Histories.Content;
    public ObservableCollection<ResourcePatchColumn> Columns { get; } = [];
    public ObservableCollection<ResourceReferenceRow> Rows { get; } = [];
    public event Action? StructureChanged;
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private bool _showDependencies = true;
    private IReadOnlyList<ResourceReferenceRow> _allRows = [];
    public ResourceOverridesPanelViewModel(ResourceOverrideAuthoringService service, IProjectService projects,
        IFileDialogService dialogs, IEditorLocalizationService text)
    {
        Service = service; _projects = projects; _dialogs = dialogs; L = text;
        _draft = service.Content.RegisterDraft(() => Columns.Any(column => column.HasDraft), CommitHeadersAsync, DiscardDrafts);
        service.Content.Changed += OnContentChanged;
        _assetChanges = service.Assets as IEditorAssetChanges;
        if (_assetChanges is not null) _assetChanges.Changed += RequestRefresh;
        projects.CurrentChanged += ProjectChanged; L.PropertyChanged += LanguageChanged;
        RequestRefresh();
    }
    partial void OnShowDependenciesChanged(bool value) => ShowRows();
    private void OnContentChanged() { if (!_notifyingDraft) RequestRefresh(); }
    private void ProjectChanged(GalNet.Editor.Abstraction.Project.GalProject? _) => RequestRefresh();
    private void LanguageChanged(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName == "Item[]") StructureChanged?.Invoke(); }
    private void NotifyDraft()
    {
        _notifyingDraft = true;
        try { Service.Content.Refresh(); } finally { _notifyingDraft = false; }
    }
    public void RequestRefresh()
    {
        Interlocked.Increment(ref _revision);
        Dispatcher.UIThread.Post(async () => await RefreshAsync());
    }
    public Task RefreshAsync()
    {
        if (_disposed) return Task.CompletedTask;
        return _refreshTask is { IsCompleted: false } pending ? pending : _refreshTask = RefreshLoopAsync();
    }
    private async Task RefreshLoopAsync()
    {
        await Task.Yield();
        if (_disposed) return;
        _refresh?.Dispose(); var request = _refresh = new();
        try
        {
            while (!_disposed)
            {
                var revision = Volatile.Read(ref _revision);
                if (_projects.Current is null) { Columns.Clear(); Rows.Clear(); StructureChanged?.Invoke(); return; }
                var snapshot = await Service.SnapshotAsync(request.Token);
                if (_disposed || request.IsCancellationRequested) return;
                if (revision != Volatile.Read(ref _revision)) continue;
                var definitions = Service.Read().Packages;
                foreach (var column in Columns.Where(column => definitions.All(definition => definition.Id != column.Id)).ToArray()) Columns.Remove(column);
                foreach (var definition in definitions)
                {
                    var column = Columns.FirstOrDefault(column => column.Id == definition.Id);
                    if (column is null) Columns.Add(new(definition, NotifyDraft)); else column.Refresh(definition);
                }
                _allRows = snapshot.Rows; ShowRows();
                var analysis = string.Join("\n", snapshot.Issues);
                if (analysis.Length > 0 || Message == _analysisMessage) Message = analysis;
                _analysisMessage = analysis;
                return;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!_disposed && !request.IsCancellationRequested) Message = EditorText.Error(error).Render(L); }
    }
    private void ShowRows()
    {
        var visible = _allRows.Where(row => ShowDependencies || row.Uses.Any(use => use.Through is null)).ToArray();
        var key = System.Text.Json.JsonSerializer.Serialize(new
        {
            rows = visible,
            columns = Columns.Select(column => new
            {
                column.Id,
                mappings = column.Mappings.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray()
            }),
            ShowDependencies
        });
        if (key == _structureKey) return;
        _structureKey = key;
        Rows.Clear(); foreach (var row in visible) Rows.Add(row);
        StructureChanged?.Invoke();
    }
    public async Task<bool> RunAsync(Func<Task> action)
    {
        if (_disposed) return false;
        try { Message = ""; await action(); return true; }
        catch (OperationCanceledException) { Message = L["Packaging.Canceled"]; return false; }
        catch (Exception error) { Message = EditorText.Error(error).Render(L); return false; }
    }
    public async Task CommitHeaderAsync(ResourcePatchColumn column)
    {
        if (!column.HasDraft) return;
        if (!int.TryParse(column.Priority, NumberStyles.Integer, CultureInfo.InvariantCulture, out var priority))
            throw new InvalidOperationException(L["Packaging.InvalidPriority"]);
        Service.SetHeader(column.Id, column.Name, priority, column.Definition.Name, column.Definition.Priority);
        // Refresh synchronously before Save All can capture this draft again.
        var definition = Service.Read().Packages.Single(item => item.Id == column.Id);
        column.Accept(definition);
        await Task.CompletedTask;
    }
    public async Task CommitHeadersAsync() { foreach (var column in Columns.ToArray()) await CommitHeaderAsync(column); }
    public void DiscardDrafts()
    {
        Columns.Clear(); foreach (var definition in Service.Read().Packages) Columns.Add(new(definition, NotifyDraft));
        StructureChanged?.Invoke(); NotifyDraft();
    }
    public async Task CreateAsync() { await RunAsync(() => { Service.Create(L["Packaging.NewPatch"]); return Task.CompletedTask; }); }
    public async Task DeleteAsync(string id) { await RunAsync(() => { Service.Delete(id); return Task.CompletedTask; }); }
    public async Task SetMappingAsync(ResourcePatchColumn column, ResourceReferenceRow row, string target)
    {
        var expected = column.Mappings.GetValueOrDefault(row.Id);
        await RunAsync(() => Service.SetMappingAsync(column.Id, row.Id, target, expected));
        await RefreshAsync();
    }
    public async Task ExportAsync(string id)
    {
        if (Busy) return;
        var target = await _dialogs.SaveFilePickerAsync(L["Packaging.Export"], "resource-" + id + ".pak");
        if (target is null || _disposed) return;
        _export = new(); Busy = true;
        await RunAsync(async () => { await CommitHeadersAsync(); await Service.ExportAsync(id, target, _export.Token); Message = L["Packaging.Exported"] + " " + target; });
        Busy = false; _export.Dispose(); _export = null;
    }
    public void CancelExport() => _export?.Cancel();
    public void Locate(ResourceReferenceRow row)
    {
        if (_projects.Current is not { } project || row.Missing) return;
        var panel = project.Services.GetRequiredService<AssetPanelViewModel>();
        project.Services.GetRequiredService<GalNet.Editor.Dock.EditorDockFactory>().OpenPanel(GalNet.Editor.Dock.EditorDockPanelIds.Assets);
        panel.Navigate(Path.GetDirectoryName("Assets/" + row.Path)?.Replace('\\', '/') ?? "Assets");
        if (panel.Entries.FirstOrDefault(item => item.Path == "Assets/" + row.Path) is { } entry) panel.Select(entry);
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _refresh?.Cancel(); _refresh?.Dispose(); _export?.Cancel();
        _draft.Dispose(); Service.Content.Changed -= OnContentChanged;
        if (_assetChanges is not null) _assetChanges.Changed -= RequestRefresh;
        _projects.CurrentChanged -= ProjectChanged; L.PropertyChanged -= LanguageChanged;
        StructureChanged = null;
    }
}
