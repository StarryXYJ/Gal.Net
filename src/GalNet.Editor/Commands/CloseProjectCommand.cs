using System;
using System.Threading.Tasks;
using GalNet.Core.Services;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Shared.Commands;
using GalNet.Editor.ViewModels;
using Serilog;

namespace GalNet.Editor.Commands;

public class CloseProjectCommand : AsyncEditorCommand
{
    private readonly IProjectService _projectService;
    private readonly INavigationService _navigation;

    public CloseProjectCommand(
        IProjectService projectService,
        INavigationService navigation,
        IEditorLocalizationService localization)
    {
        _projectService = projectService;
        _navigation = navigation;
        Id = "close_project";
        DisplayName = localization["Command.CloseProject"];
        localization.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == "Item[]")
                DisplayName = localization["Command.CloseProject"];
        };
        InitializeCommand(ExecuteAsync, () => _projectService.Current is not null);
        _projectService.CurrentChanged += _ => RelayCommand.NotifyCanExecuteChanged();
    }

    private async Task ExecuteAsync()
    {
        try
        {
            if (_projectService.Current is null)
                return;
            await _projectService.CloseAsync();
            _navigation.ResetTo<StartupPageViewModel>();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to close project");
        }
    }
}
