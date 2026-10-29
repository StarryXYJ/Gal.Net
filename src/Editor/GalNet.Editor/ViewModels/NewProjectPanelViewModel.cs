using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Contracts.Builtins;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Services;
using GalNet.Editor.Services.Interfaces;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.ViewModels;

public partial class NewProjectPanelViewModel : PageViewModelBase, IDisposable
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

    [ObservableProperty]
    private string _applicationPath = "";

    partial void OnApplicationPathChanged(string value)
    {
        _error = null;
        Message = "";
        if (string.IsNullOrWhiteSpace(value) || !string.IsNullOrWhiteSpace(ContractsDirectory)) return;
        try { ContractsDirectory = ProjectContractSource.FindForApplication(value); }
        catch (Exception error)
        {
            _error = error;
            Message = EditorText.Error(error).Render(_localization);
        }
    }

    private readonly IEditorLocalizationService _localization;
    private Exception? _error;
    public void Dispose() { _localization.PropertyChanged -= OnCultureChanged; GC.SuppressFinalize(this); }
    private void OnCultureChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName != "Item[]") return;
        Title = _localization["Startup.NewProject"];
        Message = _error is null ? "" : EditorText.Error(_error).Render(_localization);
    }

    public NewProjectPanelViewModel(
        INavigationService navigation,
        IProjectService projectService,
        IEditorPageFactory editorPageFactory,
        IFileDialogService dialogs, IEditorLocalizationService localization)
    {
        _navigation = navigation;
        _projectService = projectService;
        _editorPageFactory = editorPageFactory;
        _dialogs = dialogs;
        _localization = localization;
        _localization.PropertyChanged += OnCultureChanged;
        Title = _localization["Startup.NewProject"];
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        _error = null;
        Message = "";

        try
        {
            if (string.IsNullOrWhiteSpace(ProjectName))
                throw new EditorOperationException("project.nameRequired", "Project name is required.");
            if (string.IsNullOrWhiteSpace(ProjectRoot))
                throw new EditorOperationException("project.folderRequired", "Project folder is required.");

            var projectPath = Path.Combine(ProjectRoot, ProjectName);
            if (!TryParseResolution(Resolution, out var width, out var height))
                throw new EditorOperationException("project.resolutionInvalid", "Resolution must be in the form width×height.");
            var application = string.IsNullOrWhiteSpace(ApplicationPath) ? null : ProjectContractSource.RequireApplication(ApplicationPath);
            var useDefaultContracts = application is null && string.IsNullOrWhiteSpace(ContractsDirectory);
            var contracts = string.IsNullOrWhiteSpace(ContractsDirectory)
                ? application is null ? Path.Combine(AppContext.BaseDirectory, "DefaultContracts") : ProjectContractSource.FindForApplication(application)
                : ContractsDirectory.Trim();
            using var prepared = useDefaultContracts ? null : await Task.Run(() => ProjectContractSource.Prepare(contracts));
            var audio = GalNet.Contracts.Builtins.BuiltinAudioDefaults.Create();
            var project = await _projectService.CreateAsync(projectPath, ProjectName, new DefaultProjectOptions
            {
                DefaultWidth = width,
                DefaultHeight = height,
                AudioBuses = audio.Buses.ToList(),
                AudioTracks = audio.Tracks.ToList()
            }.ToConfiguration(), prepared?.DirectoryPath ?? contracts);
            if (application is not null)
                ExternalGameDebugClient.SaveSettings(project.RootPath, new(application, []));
            _navigation.NavigateTo(_editorPageFactory.CreateEditorPage());
        }
        catch (Exception ex)
        {
            _error = ex;
            Message = EditorText.Error(ex).Render(_localization);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task BrowseApplicationAsync()
    {
        var selected = await _dialogs.OpenFilePickerAsync(_localization["NewProject.BrowseApplication"], false);
        if (selected.Count > 0) ApplicationPath = selected[0];
    }

    [RelayCommand]
    private async Task BrowseContractsAsync()
    {
        var directory = await _dialogs.OpenFolderPickerAsync(_localization["NewProject.BrowseContracts"]);
        if (directory is not null)
        {
            ContractsDirectory = directory;
            _error = null;
            Message = "";
        }
    }

    [RelayCommand]
    private async Task BrowseProjectRootAsync()
    {
        var directory = await _dialogs.OpenFolderPickerAsync(_localization["NewProject.BrowseParent"]);
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
