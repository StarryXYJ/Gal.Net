using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Core.Services;
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
            if (!TryParseResolution(Resolution, out var width, out var height))
                throw new InvalidOperationException("Resolution must be in the form width×height.");
            await _projectService.CreateAsync(projectPath, ProjectName, new ProjectSettings { DefaultWidth = width, DefaultHeight = height });
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

    internal static bool TryParseResolution(string? value, out int width, out int height)
    {
        width = height = 0; var parts = (value ?? "").ToLowerInvariant().Replace('x', '×').Split('×', StringSplitOptions.TrimEntries);
        return parts.Length == 2 && int.TryParse(parts[0], out width) && int.TryParse(parts[1], out height)
            && width is >= 640 and <= 7680 && height is >= 480 and <= 4320;
    }
}
