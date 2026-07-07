using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Core.Services;
using GalNet.Core.Settings;
using GalNet.Editor.Project;
using GalNet.Editor.Services;

namespace GalNet.Editor.ViewModels;

public partial class NewProjectPanelViewModel : PageViewModelBase
{
    private readonly INavigationService _navigation;
    private readonly IProjectService _projectService;
    private readonly IEditorPageFactory _editorPageFactory;

    [ObservableProperty]
    private string _projectName = "NewGalProject";

    [ObservableProperty]
    private string _projectRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "GalNetProjects");

    [ObservableProperty]
    private string _message = "";

    [ObservableProperty]
    private bool _isBusy;

    public NewProjectPanelViewModel(
        INavigationService navigation,
        IProjectService projectService,
        IEditorPageFactory editorPageFactory)
    {
        _navigation = navigation;
        _projectService = projectService;
        _editorPageFactory = editorPageFactory;
        Title = "New Project";
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        Message = "";

        try
        {
            if (string.IsNullOrWhiteSpace(ProjectName))
                throw new InvalidOperationException("Project name is required.");
            if (string.IsNullOrWhiteSpace(ProjectRoot))
                throw new InvalidOperationException("Project folder is required.");

            var projectPath = Path.Combine(ProjectRoot, ProjectName);
            await _projectService.CreateAsync(projectPath, ProjectName, new ProjectSettings());
            _navigation.NavigateTo(_editorPageFactory.CreateEditorPage());
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _navigation.NavigateTo<StartupPageViewModel>();
    }
}
