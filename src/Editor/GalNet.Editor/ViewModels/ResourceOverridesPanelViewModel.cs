using System.Collections.ObjectModel;
using System.ComponentModel;
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
    public ResourceOverrideDefinition Definition { get; private set; }
    public IReadOnlyDictionary<string, string> Mappings { get; private set; }
    public string Id => Definition.Id;
    public string Name => Definition.Name;
    public int Priority => Definition.Priority;
    public ResourcePatchColumn(ResourceOverrideDefinition definition)
    { Definition = definition; Mappings = definition.Mappings; }
    public void Accept(ResourceOverrideDefinition definition)
    {
        var nameChanged = Name != definition.Name;
        var priorityChanged = Priority != definition.Priority;
        Mappings = definition.Mappings;
        Definition = definition;
        if (nameChanged) OnPropertyChanged(nameof(Name));
        if (priorityChanged) OnPropertyChanged(nameof(Priority));
    }
}

public sealed partial class ResourceOverridesPanelViewModel : ObservableObject, IUndoRedoTarget, IDisposable
{
    private readonly IProjectService _projects;
    private readonly IFileDialogService _dialogs;
    private readonly SemaphoreSlim _actions = new(1, 1);
    private readonly IEditorAssetChanges? _assetChanges;
    private CancellationTokenSource? _refresh, _export;
    private Task? _refreshTask;
    private bool _disposed;
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
    public bool CanExport => !Busy;
    partial void OnBusyChanged(bool value) => OnPropertyChanged(nameof(CanExport));
    [ObservableProperty] private bool _showDependencies = true;
    [ObservableProperty] private string _search = "";
    private IReadOnlyList<ResourceReferenceRow> _allRows = [];
    public ResourceOverridesPanelViewModel(ResourceOverrideAuthoringService service, IProjectService projects,
        IFileDialogService dialogs, IEditorLocalizationService text)
    {
        Service = service; _projects = projects; _dialogs = dialogs; L = text;
        service.Content.Changed += OnContentChanged;
        _assetChanges = service.Assets as IEditorAssetChanges;
        if (_assetChanges is not null) _assetChanges.Changed += RequestRefresh;
        projects.CurrentChanged += ProjectChanged; L.PropertyChanged += LanguageChanged;
        RequestRefresh();
    }
    partial void OnShowDependenciesChanged(bool value) => ShowRows();
    partial void OnSearchChanged(string value) => ShowRows();
    private void OnContentChanged() => RequestRefresh();
    private void ProjectChanged(GalNet.Editor.Abstraction.Project.GalProject? _) => RequestRefresh();
    private void LanguageChanged(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName == "Item[]") StructureChanged?.Invoke(); }
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
                    if (column is null) Columns.Add(new(definition)); else column.Accept(definition);
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
        var visible = _allRows.Where(row => (ShowDependencies || row.Uses.Any(use => use.Through is null)) &&
            (row.Path.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase) || row.Id.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase))).ToArray();
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
    public async Task<bool> RunAsync(Func<Task> action, bool serialize = true)
    {
        if (_disposed) return false;
        if (serialize) await _actions.WaitAsync();
        try { if (_disposed) return false; Message = ""; await action(); return true; }
        catch (OperationCanceledException) { Message = L["Packaging.Canceled"]; return false; }
        catch (Exception error) { Message = EditorText.Error(error).Render(L); return false; }
        finally { if (serialize) _actions.Release(); }
    }
    public async Task SetHeaderAsync(ResourcePatchColumn column, string name, int priority)
    {
        await RunAsync(() => Service.SetHeaderAsync(column.Id, name, priority));
        await RefreshAsync();
    }
    public async Task CreateAsync() { await RunAsync(async () => await Service.CreateAsync(L["Packaging.NewPatch"])); }
    public async Task DeleteAsync(string id) { await RunAsync(() => Service.DeleteAsync(id)); }
    public Task AddOriginalAsync(string id) => RunAsync(() => Service.AddOriginalAsync(id));
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
        await _actions.WaitAsync(); _actions.Release();
        await RunAsync(async () => { await Service.ExportAsync(id, target, _export.Token); Message = L["Packaging.Exported"] + " " + target; }, serialize: false);
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
        Service.Content.Changed -= OnContentChanged;
        if (_assetChanges is not null) _assetChanges.Changed -= RequestRefresh;
        _projects.CurrentChanged -= ProjectChanged; L.PropertyChanged -= LanguageChanged;
        StructureChanged = null;
    }
}
