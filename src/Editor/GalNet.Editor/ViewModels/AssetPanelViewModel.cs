using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Dock;
using GalNet.Editor.Models;
using GalNet.Editor.Services;
using GalNet.Editor.Services.Interfaces;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.ViewModels;

/// <summary>Project browser navigation is view state. File intents use the project's existing publisher and history.</summary>
public sealed partial class AssetPanelViewModel : ObservableObject, IDisposable
{
    private readonly ProjectFilesIndex _index;
    private readonly ProjectFileAuthoringService _files;
    private readonly ProjectTextFiles _texts;
    private readonly IProjectService _projects;
    private readonly EditorWorkspaceViewModel _workspace;
    private readonly EditorDockFactory _dock;
    private readonly IFileDialogService _dialogs;
    private readonly IAssetCatalogService _assets;
    private readonly IEditorLocalizationService _localization;
    private readonly Stack<string> _back = [], _forward = [];
    private string[] _clipboard = [];
    private bool _cut, _disposed, _rebuilding;
    private bool _committingRename;
    private bool _navigating;
    private string _searchOrigin = "";
    private Exception? _error;
    private readonly FileSystemWatcher _watcher;
    private readonly System.Threading.Timer _timer;
    private CancellationTokenSource? _thumbnails;
    private ProjectFileRequest? _delete;
    [ObservableProperty] private string _currentDirectory = "";
    [ObservableProperty] private string _pathText = "";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isEditingPath;
    [ObservableProperty] private string _errorText = "";
    [ObservableProperty] private string _renameText = "";
    [ObservableProperty] private ProjectFileItem? _renamingEntry;
    [ObservableProperty] private ProjectDirectoryNode? _selectedDirectory;
    [ObservableProperty] private double _thumbnailSize = 88;
    [ObservableProperty] private string _deleteDetails = "";
    [ObservableProperty] private bool _deleteLinkedGroups;
    [ObservableProperty] private bool _allowCandidateReferences;
    public ObservableCollection<ProjectFileItem> Entries { get; } = [];
    public ObservableCollection<ProjectFileItem> SelectedEntries { get; } = [];
    public ObservableCollection<ProjectDirectoryNode> Directories { get; } = [];
    public ObservableCollection<FileBreadcrumb> Breadcrumbs { get; } = [];
    public ProjectFileItem? SelectedEntry => SelectedEntries.FirstOrDefault();
    public bool CanGoBack => _back.Count > 0;
    public bool CanGoForward => _forward.Count > 0;
    public bool CanGoUp => CurrentDirectory.Length > 0;
    public bool CanRename => SelectedEntries.Count == 1 && SelectedEntries[0].CanEdit;
    public bool CanChangeSelection => SelectedEntries.Count > 0 && SelectedEntries.All(item => item.CanEdit);
    public double ItemWidth => ThumbnailSize + 38;
    public double ItemHeight => ThumbnailSize + 48;
    partial void OnThumbnailSizeChanged(double value) { OnPropertyChanged(nameof(ItemWidth)); OnPropertyChanged(nameof(ItemHeight)); }
    public bool IsDeletePending => _delete is not null;
    public string SelectionInformation => SelectedEntry is { } entry ? entry.Path + (entry.CanEdit ? "" : " — " + entry.ProtectionReason) : _localization.Format("Files.ItemCount", Entries.Count);
    public string ProjectRoot => _index.Root;
    public bool HasSearch => SearchText.Length > 0;
    public event Action<ProjectTextBuffer>? FileOpened;

    public AssetPanelViewModel(ProjectFilesIndex index, ProjectFileAuthoringService files, ProjectTextFiles texts, IProjectService projects, EditorWorkspaceViewModel workspace, EditorDockFactory dock, IFileDialogService dialogs, IAssetCatalogService assets, IEditorLocalizationService localization)
    {
        _index = index; _files = files; _texts = texts; _projects = projects; _workspace = workspace; _dock = dock; _dialogs = dialogs; _assets = assets; _localization = localization;
        SelectedEntries.CollectionChanged += OnSelection;
        localization.PropertyChanged += OnLocale;
        _timer = new(_ => Dispatcher.UIThread.Post(() => { if (!_disposed) Refresh(); }), null, Timeout.Infinite, Timeout.Infinite);
        _watcher = new(index.Root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite };
        _watcher.Changed += OnDiskChange; _watcher.Created += OnDiskChange; _watcher.Deleted += OnDiskChange; _watcher.Renamed += OnDiskChange;
        _watcher.EnableRaisingEvents = true;
        Refresh();
    }
    private void OnDiskChange(object? sender, FileSystemEventArgs args)
    {
        if (_disposed) return;
        if (Path.GetRelativePath(ProjectRoot, args.FullPath).StartsWith(".galnet", StringComparison.OrdinalIgnoreCase)) return;
        try { _timer.Change(300, Timeout.Infinite); } catch (ObjectDisposedException) { }
    }
    private void OnLocale(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != "Item[]") return;
        if (_error is not null) ErrorText = EditorText.Error(_error).Render(_localization);
        Refresh();
    }
    private void OnSelection(object? sender, NotifyCollectionChangedEventArgs args)
    {
        OnPropertyChanged(nameof(SelectedEntry)); OnPropertyChanged(nameof(CanRename)); OnPropertyChanged(nameof(CanChangeSelection)); OnPropertyChanged(nameof(SelectionInformation));
        if (SelectedEntry is { IsDirectory: false } item && item.Path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) && _assets.GetEntry(item.Path[7..]) is { } asset) _workspace.FocusAsset(asset);
        else _workspace.SelectedAsset = null;
    }
    partial void OnSelectedDirectoryChanged(ProjectDirectoryNode? value) { if (!_rebuilding && value is not null) Navigate(value.Path); }
    partial void OnSearchTextChanging(string value) { if (!HasSearch && value.Length > 0) _searchOrigin = CurrentDirectory; }
    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasSearch));
        if (_navigating) return;
        if (value.Length > 0) { LoadEntries(); return; }
        var selected = SelectedEntry?.Path;
        Navigate(selected is null ? _searchOrigin : Path.GetDirectoryName(selected)?.Replace('\\', '/') ?? "");
        if (selected is not null && Entries.FirstOrDefault(item => ProjectFilePaths.Comparer.Equals(item.Path, selected)) is { } entry) Select(entry);
    }
    [RelayCommand] private void ClearSearch() => SearchText = "";
    partial void OnCurrentDirectoryChanged(string value)
    {
        PathText = value; Breadcrumbs.Clear(); Breadcrumbs.Add(new(_projects.Current!.Name, ""));
        var path = "";
        foreach (var part in value.Split('/', StringSplitOptions.RemoveEmptyEntries)) { path = path.Length == 0 ? part : path + "/" + part; Breadcrumbs.Add(new(part, path)); }
        OnPropertyChanged(nameof(CanGoUp));
    }
    private void ShowError(Exception error) { _error = error; ErrorText = EditorText.Error(error).Render(_localization); }
    public void Navigate(string path, bool record = true)
    {
        try
        {
            path = path.Trim().Replace('\\', '/').TrimEnd('/');
            if (path.Length > 0) ProjectFilePaths.Normalize(path);
            if (!_index.DirectoryExists(path)) throw ProjectFilePaths.Error("missing", path);
            if (CurrentDirectory != path && record) { _back.Push(CurrentDirectory); _forward.Clear(); }
            _navigating = true;
            try { CurrentDirectory = path; SearchText = ""; PathText = path; IsEditingPath = false; }
            finally { _navigating = false; }
            CancelRename(); LoadEntries(); SelectTreePath();
            OnPropertyChanged(nameof(CanGoBack)); OnPropertyChanged(nameof(CanGoForward));
        }
        catch (Exception error) { PathText = CurrentDirectory; ShowError(error); }
    }
    [RelayCommand] private void NavigateDirectory(string path) => Navigate(path);
    [RelayCommand] private void NavigatePath(string? path) => Navigate(path ?? "");
    [RelayCommand] private void Back() { if (_back.TryPop(out var path)) { _forward.Push(CurrentDirectory); Navigate(path, false); } }
    [RelayCommand] private void Forward() { if (_forward.TryPop(out var path)) { _back.Push(CurrentDirectory); Navigate(path, false); } }
    [RelayCommand] private void Up() => Navigate(Path.GetDirectoryName(CurrentDirectory)?.Replace('\\', '/') ?? "");
    private void SelectTreePath()
    {
        if (Directories.Count == 0) return;
        var node = Directories[0]; node.IsExpanded = true;
        var path = "";
        foreach (var part in CurrentDirectory.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            path = path.Length == 0 ? part : path + "/" + part; node.EnsureChildren();
            var next = node.Children.FirstOrDefault(child => ProjectFilePaths.Comparer.Equals(child.Path, path));
            if (next is null) break; node = next; node.IsExpanded = true;
        }
        _rebuilding = true; SelectedDirectory = node; _rebuilding = false;
    }
    private static IEnumerable<ProjectDirectoryNode> Expanded(ProjectDirectoryNode node) => node.IsExpanded ? new[] { node }.Concat(node.Children.SelectMany(Expanded)) : [];
    [RelayCommand]
    public void Refresh()
    {
        if (_disposed || _projects.Current?.RootPath != ProjectRoot) return;
        if (_projects.Current!.Services.GetService(typeof(IProjectContentSession)) is IProjectContentSession { IsPublishing: true }) { _timer.Change(100, Timeout.Infinite); return; }
        try
        {
            while (!_index.DirectoryExists(CurrentDirectory) && CurrentDirectory.Length > 0) CurrentDirectory = Path.GetDirectoryName(CurrentDirectory)?.Replace('\\', '/') ?? "";
            var expanded = Directories.SelectMany(Expanded).Select(node => node.Path).ToHashSet(ProjectFilePaths.Comparer);
            var root = new ProjectDirectoryNode("", _projects.Current.Name, _index.Directories) { IsExpanded = true };
            Directories.Clear(); Directories.Add(root);
            void Expand(ProjectDirectoryNode node) { if (!expanded.Contains(node.Path)) return; node.IsExpanded = true; foreach (var child in node.Children) Expand(child); }
            Expand(root); SelectTreePath(); OnCurrentDirectoryChanged(CurrentDirectory); LoadEntries();
            foreach (var file in _projects.Current.Services.GetService(typeof(IProjectContentSession)) is IProjectContentSession content ? content.FileDocuments.Keys : [])
                try { _texts.Get(file).CheckExternalChange(); } catch (KeyNotFoundException) { }
        }
        catch (Exception error) { ShowError(error); }
    }
    private void LoadEntries()
    {
        if (_disposed) return;
        var selected = SelectedEntries.Select(item => item.Path).ToHashSet(ProjectFilePaths.Comparer);
        _thumbnails?.Cancel(); _thumbnails?.Dispose(); _thumbnails = new();
        try
        {
            var incoming = (HasSearch ? _index.Search(SearchText) : _index.Read(CurrentDirectory)).ToArray();
            foreach (var item in Entries.Where(item => !incoming.Any(next => next.Path == item.Path && next.Type == item.Type && next.WriteTimeUtc == item.WriteTimeUtc && next.ProtectionReason == item.ProtectionReason)).ToArray())
            { SelectedEntries.Remove(item); Entries.Remove(item); item.Dispose(); }
            for (var index = 0; index < incoming.Length; index++)
            {
                var next = incoming[index]; var item = Entries.FirstOrDefault(item => item.Path == next.Path);
                if (item is null) { Entries.Insert(index, next); item = next; } else { if (Entries.IndexOf(item) != index) Entries.Move(Entries.IndexOf(item), index); next.Dispose(); }
                if (selected.Contains(item.Path) && !SelectedEntries.Contains(item)) SelectedEntries.Add(item);
            }
            _ = ThumbnailsAsync(Entries.ToArray(), _thumbnails.Token);
        }
        catch (Exception error) { ShowError(error); }
        OnPropertyChanged(nameof(SelectionInformation));
    }
    private async Task ThumbnailsAsync(ProjectFileItem[] items, CancellationToken cancellationToken)
    {
        foreach (var item in items.Where(item => item.Type == "sprite" && item.Thumbnail is null))
            try
            {
                var bitmap = await Task.Run(() => { using var stream = File.OpenRead(item.FullPath); return Bitmap.DecodeToWidth(stream, 180); }, cancellationToken);
                if (_disposed || cancellationToken.IsCancellationRequested || !Entries.Contains(item)) bitmap.Dispose(); else item.Thumbnail = bitmap;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception) { }
    }
    [RelayCommand]
    private void Open(ProjectFileItem? item)
    {
        item ??= SelectedEntry; if (item is null) return;
        if (item.IsDirectory) { Navigate(item.Path); return; }
        if (item.Path == "Graph/graph.json") { _dock.OpenPanel(EditorDockPanelIds.NodeGraph); return; }
        if (item.Path == "settings.json") { (_projects.Current!.Services.GetService(typeof(IEditorWindowFactory)) as IEditorWindowFactory)?.CreateProjectSettingsWindow().Show(); return; }
        var group = _workspace.Nodes.FirstOrDefault(node => node.NodeKind == Models.Graph.GraphNodeKind.LinearGroup && ProjectFilePaths.Comparer.Equals("Graph/" + node.Source.File, item.Path));
        if (group is not null) { _workspace.OpenGroupEditor(group); return; }
        try { var buffer = _texts.Open(item.Path); _dock.OpenFileEditor(buffer); FileOpened?.Invoke(buffer); }
        catch (Exception error) { ShowError(error); }
    }
    public void Select(ProjectFileItem item) { if (!SelectedEntries.Contains(item)) { SelectedEntries.Clear(); SelectedEntries.Add(item); } }
    [RelayCommand] private void CopySelected() { if (CanChangeSelection) { _clipboard = SelectedEntries.Select(item => item.Path).ToArray(); _cut = false; } }
    [RelayCommand] private void CutSelected() { CopySelected(); _cut = true; }
    [RelayCommand] private Task DuplicateAsync() => CanChangeSelection ? ExecuteAsync(new(ProjectFileAction.Copy, SelectedEntries.Select(item => item.Path).ToArray(), CurrentDirectory, UniqueNames: true)) : Task.CompletedTask;
    [RelayCommand] private async Task PasteAsync() { if (_clipboard.Length == 0) return; if (await ExecuteAsync(new(_cut ? ProjectFileAction.Move : ProjectFileAction.Copy, _clipboard, CurrentDirectory, UniqueNames: !_cut)) && _cut) _clipboard = []; }
    [RelayCommand] private async Task ImportAsync() { var paths = await _dialogs.OpenFilePickerAsync(_localization["Files.Import"]); if (paths.Count > 0) await ImportExternalAsync(paths); }
    public Task<bool> ImportExternalAsync(IReadOnlyList<string> paths, string? target = null) => ExecuteAsync(new(ProjectFileAction.Import, paths, target ?? CurrentDirectory, UniqueNames: true));
    private string FreeName(string name)
    {
        var target = CurrentDirectory.Length == 0 ? name : CurrentDirectory + "/" + name; var original = target; var number = 1;
        while (File.Exists(Path.Combine(ProjectRoot, target)) || Directory.Exists(Path.Combine(ProjectRoot, target))) target = original[..^Path.GetExtension(original).Length] + " " + number++ + Path.GetExtension(original);
        return target;
    }
    [RelayCommand] private Task NewFolderAsync() => CreateAsync(true);
    [RelayCommand] private Task NewFileAsync() => CreateAsync(false);
    private async Task CreateAsync(bool directory)
    {
        var target = FreeName(_localization[directory ? "Files.NewFolderName" : "Files.NewFileName"]);
        if (!await ExecuteAsync(new(directory ? ProjectFileAction.CreateDirectory : ProjectFileAction.CreateFile, [], target))) return;
        var item = Entries.FirstOrDefault(item => item.Path == target); if (item is not null) { Select(item); BeginRename(item); }
    }
    [RelayCommand] private void BeginRename(ProjectFileItem? item) { item ??= SelectedEntry; if (item?.CanEdit != true) return; CancelRename(); Select(item); RenamingEntry = item; RenameText = item.Name; item.IsRenaming = true; }
    [RelayCommand]
    public async Task CommitRenameAsync()
    {
        if (_committingRename || RenamingEntry is not { } item) return;
        var name = RenameText?.Trim() ?? "";
        if (name == item.Name) { CancelRename(); return; }
        _committingRename = true;
        try { if (name.Contains('/') || name.Contains('\\')) throw ProjectFilePaths.Error("path", name); var parent = Path.GetDirectoryName(item.Path)?.Replace('\\', '/'); var target = string.IsNullOrEmpty(parent) ? name : parent + "/" + name; if (await ExecuteAsync(new(ProjectFileAction.Move, [item.Path], target, false))) { CancelRename(); if (Entries.FirstOrDefault(next => next.Path == target) is { } next) Select(next); } }
        catch (Exception error) { ShowError(error); }
        finally { _committingRename = false; }
    }
    [RelayCommand] private void CancelRename() { if (RenamingEntry is not null) RenamingEntry.IsRenaming = false; RenamingEntry = null; RenameText = ""; }
    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        if (!CanChangeSelection) return;
        try
        {
            var request = new ProjectFileRequest(ProjectFileAction.Delete, SelectedEntries.Select(item => item.Path).ToArray());
            var impact = _files.Analyze(request);
            _delete = request; DeleteLinkedGroups = false; AllowCandidateReferences = false;
            DeleteDetails = string.Join("\n", request.Sources.Concat(impact.Groups.Select(id => "Group: " + id)).Concat(impact.References.Select(reference => reference.Document + "/" + reference.Field)));
            OnPropertyChanged(nameof(IsDeletePending));
        }
        catch (Exception error) { ShowError(error); }
        await Task.CompletedTask;
    }
    [RelayCommand] private async Task ConfirmDeleteAsync() { if (_delete is not null && await ExecuteAsync(_delete with { DeleteGroups = DeleteLinkedGroups, AllowCandidateReferences = AllowCandidateReferences })) CancelDelete(); }
    [RelayCommand] private void CancelDelete() { _delete = null; DeleteDetails = ""; OnPropertyChanged(nameof(IsDeletePending)); }
    public bool CanDrop(IEnumerable<string> sources, string target, bool copy = false)
    {
        try
        {
            return CanImportInto(target) && sources.All(source => ProjectFilePaths.Editable(source).Length > 0 &&
                ProjectFilePaths.Category(source) == ProjectFilePaths.Category((target.Length == 0 ? "" : target + "/") + Path.GetFileName(source)) &&
                !ProjectFilePaths.Within(target, source) && (copy || !ProjectFilePaths.Comparer.Equals(Path.GetDirectoryName(source)?.Replace('\\', '/') ?? "", target)));
        }
        catch (Exception) { return false; }
    }
    public bool CanImportInto(string directory) { try { ProjectFilePaths.Editable((directory.Length == 0 ? "" : directory + "/") + "file.tmp"); return _index.DirectoryExists(directory); } catch (Exception) { return false; } }
    public Task<bool> DropAsync(string[] sources, string target, bool copy) => ExecuteAsync(new(copy ? ProjectFileAction.Copy : ProjectFileAction.Move, sources, target, UniqueNames: copy));
    private async Task<bool> ExecuteAsync(ProjectFileRequest request)
    {
        try { _error = null; ErrorText = ""; await _files.ExecuteAsync(request); Refresh(); return true; }
        catch (Exception error) { ShowError(error); return false; }
    }
    [RelayCommand]
    private void ShowInExplorer(ProjectFileItem? item)
    {
        try
        {
            var path = item?.FullPath ?? Path.Combine(ProjectRoot, CurrentDirectory);
            var info = OperatingSystem.IsWindows() && File.Exists(path)
                ? new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
                : new System.Diagnostics.ProcessStartInfo(Directory.Exists(path) ? path : Path.GetDirectoryName(path)!);
            info.UseShellExecute = true; System.Diagnostics.Process.Start(info);
        }
        catch (Exception error) { ShowError(error); }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _watcher.Dispose(); _timer.Dispose(); _thumbnails?.Cancel(); _thumbnails?.Dispose();
        SelectedEntries.CollectionChanged -= OnSelection; _localization.PropertyChanged -= OnLocale; foreach (var item in Entries) item.Dispose();
    }
}

public sealed record FileBreadcrumb(string Name, string Path);
