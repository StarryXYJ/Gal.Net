using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Threading.Tasks;
using System.Threading;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Editor.Models;
using GalNet.Editor.Services.Interfaces;
using GalNet.Editor.Abstraction.Services;
using System.Diagnostics;
using Avalonia.Media.Imaging;

namespace GalNet.Editor.ViewModels;

public sealed partial class AssetPanelViewModel : ObservableObject, IDisposable
{
    private readonly IAssetCatalogService _catalog;
    private readonly EditorWorkspaceViewModel _workspace;
    private readonly IFileDialogService _fileDialogs;
    private readonly IEditorLocalizationService _localization;
    private AssetEntry? _clipboardEntry;
    private bool _isCut;
    private CancellationTokenSource? _thumbnailCancellation;
    [ObservableProperty] private string _currentDirectory = "";
    [ObservableProperty] private string _pathText = "Assets";
    [ObservableProperty] private AssetEntry? _selectedEntry;
    [ObservableProperty] private string _errorText = "";
    [ObservableProperty] private bool _isExternalDropActive;
    [ObservableProperty] private AssetEntry? _renamingEntry;
    [ObservableProperty] private string _renameText = "";
    [ObservableProperty] private AssetEntry? _dropTargetEntry;
    [ObservableProperty] private bool _isParentDropTarget;
    public ObservableCollection<AssetEntry> Entries { get; } = [];
    public string Breadcrumb => string.IsNullOrEmpty(CurrentDirectory) ? "Assets" : "Assets / " + CurrentDirectory;
    public bool CanGoBack => !string.IsNullOrEmpty(CurrentDirectory);

    public AssetPanelViewModel(IAssetCatalogService catalog, EditorWorkspaceViewModel workspace, IFileDialogService fileDialogs, IEditorLocalizationService localization)
    {
        _catalog = catalog; _workspace = workspace; _fileDialogs = fileDialogs; _localization = localization; _catalog.Changed += OnCatalogChanged; _ = RefreshAsync();
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
        if (entry is null) return;
        if (!entry.IsDirectory)
        {
            try { Process.Start(new ProcessStartInfo(entry.FullPath) { UseShellExecute = true }); }
            catch (Exception ex) { ErrorText = ex.Message; }
            return;
        }
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
    [RelayCommand] private Task ImportExternalAsync(IEnumerable<string> paths) => ImportExternalCoreAsync(paths);
    public void SetExternalDropActive(bool value) => IsExternalDropActive = value;
    private async Task ImportExternalCoreAsync(IEnumerable<string> paths)
    {
        try { ErrorText = ""; await _catalog.ImportExternalAsync(paths, CurrentDirectory); LoadEntries(); }
        catch (Exception ex) { ErrorText = ex.Message; }
    }
    [RelayCommand] private void CopySelected()
    {
        _clipboardEntry = SelectedEntry;
        _isCut = false;
    }
    [RelayCommand] private void CutSelected()
    {
        _clipboardEntry = SelectedEntry;
        _isCut = true;
    }
    [RelayCommand] private async Task PasteAsync()
    {
        if (_clipboardEntry is null) return;
        try
        {
            if (_isCut) await _catalog.MoveAsync(_clipboardEntry.RelativePath, CurrentDirectory);
            else if (!_clipboardEntry.IsDirectory) await _catalog.ImportAsync([_clipboardEntry.FullPath], CurrentDirectory);
            _clipboardEntry = null; _isCut = false; LoadEntries();
        }
        catch (Exception ex) { ErrorText = ex.Message; }
    }
    [RelayCommand] private async Task NewFolderAsync()
    {
        try { await _catalog.CreateDirectoryAsync(CurrentDirectory, "New Folder"); LoadEntries(); }
        catch (Exception ex) { ErrorText = ex.Message; }
    }
    [RelayCommand] private void BeginRename(AssetEntry? entry)
    {
        if (entry is null) return;
        CancelRename();
        SelectedEntry = entry;
        RenamingEntry = entry;
        RenameText = entry.Name;
        entry.IsRenaming = true;
    }
    [RelayCommand] private async Task CommitRenameAsync(AssetEntry? entry)
    {
        entry ??= RenamingEntry;
        if (entry is null || !ReferenceEquals(entry, RenamingEntry)) return;
        var newName = RenameText.Trim();
        if (string.Equals(entry.Name, newName, StringComparison.Ordinal)) { CancelRename(); return; }
        try
        {
            ErrorText = "";
            await _catalog.RenameAsync(entry.RelativePath, newName);
            CancelRename();
            LoadEntries();
        }
        catch (ArgumentException) { ErrorText = _localization["Asset.Error.InvalidName"]; }
        catch (Exception) { ErrorText = _localization["Asset.Error.RenameFailed"]; }
    }
    [RelayCommand] private void CancelRename()
    {
        if (RenamingEntry is not null) RenamingEntry.IsRenaming = false;
        RenamingEntry = null;
        RenameText = "";
    }
    [RelayCommand] private void ShowInExplorer(AssetEntry? entry)
    {
        var path = entry?.FullPath;
        if (string.IsNullOrWhiteSpace(path)) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", entry!.IsDirectory ? $"\"{path}\"" : $"/select,\"{path}\"") { UseShellExecute = true }); }
        catch (Exception ex) { ErrorText = ex.Message; }
    }
    [RelayCommand] private void ShowCurrentDirectoryInExplorer()
    {
        try
        {
            var entry = _catalog.GetEntry(CurrentDirectory);
            if (entry is not null) ShowInExplorer(entry);
        }
        catch (Exception ex) { ErrorText = ex.Message; }
    }
    [RelayCommand] private Task DeleteSelected() => DeleteSelectedAsync();
    private async Task DeleteSelectedAsync()
    {
        if (SelectedEntry is null) return;
        try { await _catalog.DeleteAsync(SelectedEntry.RelativePath); SelectedEntry = null; LoadEntries(); }
        catch (Exception ex) { ErrorText = ex.Message; }
    }
    public bool CanMoveTo(AssetEntry target, string sourceRelativePath)
    {
        if (!target.IsDirectory || string.IsNullOrWhiteSpace(sourceRelativePath)) return false;
        return CanMoveToDirectory(target.RelativePath, sourceRelativePath);
    }
    private static bool CanMoveToDirectory(string targetDirectory, string sourceRelativePath)
    {
        var targetPath = targetDirectory.TrimEnd('/');
        var sourcePath = sourceRelativePath.TrimEnd('/');
        if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(Path.GetDirectoryName(sourcePath)?.Replace('\\', '/'), targetPath, StringComparison.OrdinalIgnoreCase)) return false;
        return !targetPath.StartsWith(sourcePath + "/", StringComparison.OrdinalIgnoreCase);
    }
    public bool CanMoveToParent(string sourceRelativePath) =>
        CanGoBack && CanMoveToDirectory(GetParentDirectory(), sourceRelativePath);
    public void SetDropTarget(AssetEntry? entry)
    {
        if (ReferenceEquals(DropTargetEntry, entry)) return;
        if (DropTargetEntry is not null) DropTargetEntry.IsDropTarget = false;
        DropTargetEntry = entry;
        if (DropTargetEntry is not null) DropTargetEntry.IsDropTarget = true;
    }
    public async Task MoveInternalAsync(string sourceRelativePath, AssetEntry target)
    {
        if (!CanMoveTo(target, sourceRelativePath)) return;
        try { ErrorText = ""; await _catalog.MoveAsync(sourceRelativePath, target.RelativePath); LoadEntries(); }
        catch (Exception) { ErrorText = _localization["Asset.Error.MoveFailed"]; }
        finally { SetDropTarget(null); }
    }
    public void SetParentDropTarget(bool value) => IsParentDropTarget = value;
    public async Task MoveInternalToParentAsync(string sourceRelativePath)
    {
        if (!CanMoveToParent(sourceRelativePath)) return;
        try { ErrorText = ""; await _catalog.MoveAsync(sourceRelativePath, GetParentDirectory()); LoadEntries(); }
        catch (Exception) { ErrorText = _localization["Asset.Error.MoveFailed"]; }
        finally { IsParentDropTarget = false; }
    }
    private string GetParentDirectory()
    {
        var slash = CurrentDirectory.LastIndexOf('/');
        return slash < 0 ? "" : CurrentDirectory[..slash];
    }
    private void OnCatalogChanged() => Avalonia.Threading.Dispatcher.UIThread.Post(LoadEntries);
    private void LoadEntries()
    {
        SetDropTarget(null);
        CancelRename();
        _thumbnailCancellation?.Cancel();
        _thumbnailCancellation?.Dispose();
        _thumbnailCancellation = new CancellationTokenSource();
        Entries.Clear();
        foreach (var item in _catalog.GetDirectory(CurrentDirectory)) Entries.Add(item);
        _ = LoadThumbnailsAsync(Entries.ToArray(), _thumbnailCancellation.Token);
    }
    private async Task LoadThumbnailsAsync(IEnumerable<AssetEntry> entries, CancellationToken cancellationToken)
    {
        foreach (var entry in entries)
        {
            try
            {
                var path = await _catalog.GetThumbnailPathAsync(entry, cancellationToken);
                if (path is null || cancellationToken.IsCancellationRequested) continue;
                var thumbnail = await Task.Run(() => new Bitmap(path), cancellationToken);
                if (cancellationToken.IsCancellationRequested || !Entries.Contains(entry)) { thumbnail.Dispose(); continue; }
                entry.Thumbnail = thumbnail;
            }
            catch (OperationCanceledException) { return; }
            catch { entry.Thumbnail = null; }
        }
    }
    public void Dispose()
    {
        _catalog.Changed -= OnCatalogChanged;
        _thumbnailCancellation?.Cancel();
        _thumbnailCancellation?.Dispose();
        foreach (var entry in Entries) entry.Thumbnail?.Dispose();
    }
}
