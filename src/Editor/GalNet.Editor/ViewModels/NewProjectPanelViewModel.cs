using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Core.Settings;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Services;
using GalNet.Editor.Services.Interfaces;

namespace GalNet.Editor.ViewModels;

public partial class NewProjectPanelViewModel : PageViewModelBase
{
    public static IReadOnlyList<string> ResolutionPresets { get; } = ["1280×720", "1280×800", "1280×920", "1366×768", "1600×900", "1920×1080", "2560×1440", "3840×2160"];
    private readonly INavigationService _navigation;
    private readonly IProjectService _projectService;
    private readonly IEditorPageFactory _editorPageFactory;
    private readonly IFileDialogService _dialogs;

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

    [ObservableProperty]
    private string _resolution = "1920×1080";

    [ObservableProperty]
    private string _contractsDirectory = "";


    public NewProjectPanelViewModel(
        INavigationService navigation,
        IProjectService projectService,
        IEditorPageFactory editorPageFactory,
        IFileDialogService dialogs)
    {
        _navigation = navigation;
        _projectService = projectService;
        _editorPageFactory = editorPageFactory;
        _dialogs = dialogs;
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
            if (!TryParseResolution(Resolution, out var width, out var height))
                throw new InvalidOperationException("Resolution must be in the form width×height.");
            var contracts = string.IsNullOrWhiteSpace(ContractsDirectory)
                ? Path.Combine(AppContext.BaseDirectory, "DefaultContracts")
                : ContractsDirectory;
            await _projectService.CreateAsync(projectPath, ProjectName, new ProjectSettings { DefaultWidth = width, DefaultHeight = height }, contracts);
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
    private async Task BrowseContractsAsync()
    {
        var directory = await _dialogs.OpenFolderPickerAsync("Select built project contract directory");
        if (directory is not null) ContractsDirectory = directory;
    }

    [RelayCommand]
    private async Task BrowseProjectRootAsync()
    {
        var directory = await _dialogs.OpenFolderPickerAsync("Select project parent folder");
        if (directory is not null) ProjectRoot = directory;
    }

    [RelayCommand]
    private void Cancel()
    {
        _navigation.NavigateTo<StartupPageViewModel>();
    }

    internal static bool TryParseResolution(string? value, out int width, out int height)
    {
        width = height = 0; var parts = (value ?? "").ToLowerInvariant().Replace('x', '×').Split('×', StringSplitOptions.TrimEntries);
        return parts.Length == 2 && int.TryParse(parts[0], out width) && int.TryParse(parts[1], out height)
            && width is >= 640 and <= 7680 && height is >= 480 and <= 4320;
    }
}
