using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Editor.Abstraction.Project;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Services;
using GalNet.Editor.Services.Interfaces;
using Serilog;

namespace GalNet.Editor.ViewModels;

public partial class StartupPageViewModel : PageViewModelBase, IDisposable
{
    private readonly INavigationService _navigation;
    private readonly IProjectService _projectService;
    private readonly IEditorPageFactory _editorPageFactory;
    private readonly IFileDialogService _fileDialog;
    private Exception? _error;
    private void OnCultureChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName != "Item[]") return;
        Title = L["App.Title"];
        ErrorText = _error is null ? "" : EditorText.Error(_error).Render(L);
    }
    public void Dispose() { L.PropertyChanged -= OnCultureChanged; GC.SuppressFinalize(this); }
    public IEditorLocalizationService L { get; }

    public StartupPageViewModel(
        INavigationService navigation,
        IProjectService projectService,
        IEditorPageFactory editorPageFactory,
        IFileDialogService fileDialog,
        IEditorLocalizationService localization)
    {
        _navigation = navigation;
        _projectService = projectService;
        _editorPageFactory = editorPageFactory;
        _fileDialog = fileDialog;
        L = localization;
        Title = L["App.Title"];
        L.PropertyChanged += OnCultureChanged;

        RefreshRecentProjects();
    }

    public ObservableCollection<RecentProjectItem> RecentProjects { get; } = [];
    [ObservableProperty] private string _errorText = "";

    private void RefreshRecentProjects()
    {
        RecentProjects.Clear();
        foreach (var info in _projectService.GetRecentProjects())
        {
            RecentProjects.Add(new RecentProjectItem
            {
                // Records created before path normalization may have an empty Name
                // when the folder picker returned a trailing directory separator.
                Name = string.IsNullOrWhiteSpace(info.Name)
                    ? Path.GetFileName(Path.TrimEndingDirectorySeparator(info.Path))
                    : info.Name,
                Path = info.Path,
                LastOpened = info.LastOpened.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture)
            });
        }
    }

    [RelayCommand]
    private void NewProject()
    {
        _navigation.NavigateTo<NewProjectPanelViewModel>();
    }

    [RelayCommand]
    private async Task OpenProjectAsync()
    {
        _error = null;
        ErrorText = "";
        try
        {
            var projectPath = await _fileDialog.OpenFolderPickerAsync(L["NewProject.BrowseParent"]);
            if (projectPath == null) return;

            var project = await _projectService.OpenAsync(projectPath);
            NavigateToEditor(project);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open project");
            _error = ex;
            ErrorText = EditorText.Error(ex).Render(L);
        }
    }

    [RelayCommand]
    private async Task OpenRecentProjectAsync(RecentProjectItem? item)
    {
        _error = null;
        ErrorText = "";
        if (item == null) return;

        try
        {
            if (!Directory.Exists(item.Path))
            {
                Log.Warning("Recent project path no longer exists: {Path}", item.Path);
                _error = new EditorOperationException("project.folderMissing", "Project folder not found.", item.Path);
                ErrorText = EditorText.Error(_error).Render(L);
                _projectService.RemoveRecentProject(item.Path);
                RefreshRecentProjects();
                return;
            }

            var project = await _projectService.OpenAsync(item.Path);
            NavigateToEditor(project);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open recent project: {Path}", item.Path);
            _error = ex;
            ErrorText = EditorText.Error(ex).Render(L);
        }
    }

    [RelayCommand]
    private void RemoveRecentProject(RecentProjectItem? item)
    {
        if (item == null) return;
        _projectService.RemoveRecentProject(item.Path);
        RefreshRecentProjects();
    }

    private void NavigateToEditor(GalProject project)
    {
        _navigation.NavigateTo(_editorPageFactory.CreateEditorPage());
    }
}

public partial class RecentProjectItem : ObservableObject
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string LastOpened { get; set; } = "";
}
