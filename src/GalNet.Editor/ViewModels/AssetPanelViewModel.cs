using System.Collections.ObjectModel;
using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Editor.Models;
using GalNet.Editor.Services.Interfaces;
using GalNet.Editor.Abstraction.Services;

namespace GalNet.Editor.ViewModels;

public sealed partial class AssetPanelViewModel : ObservableObject, IDisposable
{
    private readonly IAssetCatalogService _catalog;
    private readonly EditorWorkspaceViewModel _workspace;
    private readonly IFileDialogService _fileDialogs;
    [ObservableProperty] private string _currentDirectory = "";
    [ObservableProperty] private string _pathText = "Assets";
    [ObservableProperty] private AssetEntry? _selectedEntry;
    [ObservableProperty] private string _errorText = "";
    public ObservableCollection<AssetEntry> Entries { get; } = [];
    public string Breadcrumb => string.IsNullOrEmpty(CurrentDirectory) ? "Assets" : "Assets / " + CurrentDirectory;
    public bool CanGoBack => !string.IsNullOrEmpty(CurrentDirectory);

    public AssetPanelViewModel(IAssetCatalogService catalog, EditorWorkspaceViewModel workspace, IFileDialogService fileDialogs)
    {
        _catalog = catalog; _workspace = workspace; _fileDialogs = fileDialogs; _catalog.Changed += OnCatalogChanged; _ = RefreshAsync();
    }
    partial void OnCurrentDirectoryChanged(string value) { PathText = string.IsNullOrEmpty(value) ? "Assets" : "Assets/" + value; OnPropertyChanged(nameof(Breadcrumb)); OnPropertyChanged(nameof(CanGoBack)); }
    partial void OnSelectedEntryChanged(AssetEntry? value) { if (value is { IsDirectory: false }) _workspace.FocusAsset(value); }
    [RelayCommand] private Task Refresh() => RefreshAsync();
    private async Task RefreshAsync()
    {
        try { ErrorText = ""; await _catalog.RefreshAsync(); LoadEntries(); }
        catch (Exception ex) { ErrorText = ex.Message; }
    }
    [RelayCommand] private void Open(AssetEntry? entry)
    {
        if (entry?.IsDirectory != true) return;
        CurrentDirectory = entry.RelativePath; SelectedEntry = null; LoadEntries();
    }
    [RelayCommand] private void Back()
    {
        if (!CanGoBack) return;
        var slash = CurrentDirectory.LastIndexOf('/'); CurrentDirectory = slash < 0 ? "" : CurrentDirectory[..slash]; SelectedEntry = null; LoadEntries();
    }
    [RelayCommand] private void NavigatePath(string? input)
    {
        var original = CurrentDirectory;
        var path = (input ?? "").Trim().Replace('\\', '/').Trim('/');
        if (path.Equals("Assets", StringComparison.OrdinalIgnoreCase)) path = "";
        else if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) path = path[7..];
        else { PathText = string.IsNullOrEmpty(original) ? "Assets" : "Assets/" + original; ErrorText = "Path must start with Assets"; return; }
        if (!_catalog.DirectoryExists(path)) { PathText = string.IsNullOrEmpty(original) ? "Assets" : "Assets/" + original; ErrorText = "Folder does not exist"; return; }
        ErrorText = ""; CurrentDirectory = path; SelectedEntry = null; LoadEntries();
    }
    [RelayCommand] private async Task ImportAsync()
    {
        var files = await _fileDialogs.OpenFilePickerAsync("Import Assets");
        if (files.Count == 0) return;
        try { await _catalog.ImportAsync(files, CurrentDirectory); LoadEntries(); }
        catch (Exception ex) { ErrorText = ex.Message; }
    }
    [RelayCommand] private Task DeleteSelected() => DeleteSelectedAsync();
    private async Task DeleteSelectedAsync()
    {
        if (SelectedEntry is null) return;
        try { await _catalog.DeleteAsync(SelectedEntry.RelativePath); SelectedEntry = null; LoadEntries(); }
        catch (Exception ex) { ErrorText = ex.Message; }
    }
    private void OnCatalogChanged() => Avalonia.Threading.Dispatcher.UIThread.Post(LoadEntries);
    private void LoadEntries() { Entries.Clear(); foreach (var item in _catalog.GetDirectory(CurrentDirectory)) Entries.Add(item); }
    public void Dispose() => _catalog.Changed -= OnCatalogChanged;
}
