using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Core.Services;
using GalNet.Editor.Abstraction.Project;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Services;
using GalNet.Editor.Services.Interfaces;
using Serilog;

namespace GalNet.Editor.ViewModels;

public partial class StartupPageViewModel : PageViewModelBase
{
    private readonly INavigationService _navigation;
    private readonly IProjectService _projectService;
    private readonly IEditorPageFactory _editorPageFactory;
    private readonly IFileDialogService _fileDialog;
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
        L.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == "Item[]")
                Title = L["App.Title"];
        };

        RefreshRecentProjects();
    }

    public ObservableCollection<RecentProjectItem> RecentProjects { get; } = [];

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
                LastOpened = info.LastOpened.ToString("yyyy-MM-dd HH:mm")
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
        try
        {
            var projectPath = await _fileDialog.OpenFolderPickerAsync("Select project folder");
            if (projectPath == null) return;

            var project = await _projectService.OpenAsync(projectPath);
            NavigateToEditor(project);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open project");
        }
    }

    [RelayCommand]
    private async Task OpenRecentProjectAsync(RecentProjectItem? item)
    {
        if (item == null) return;

        try
        {
            if (!Directory.Exists(item.Path))
            {
                Log.Warning("Recent project path no longer exists: {Path}", item.Path);
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
