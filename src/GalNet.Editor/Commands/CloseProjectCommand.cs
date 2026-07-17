using System;
using System.Threading.Tasks;
using GalNet.Core.Services;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.ViewModels;
using GalNet.Core.I18n;
using Avalonia.Input;
using Serilog;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Editor.Commands;

public sealed class CloseProjectCommand : AsyncEditorUiCommand
{
    private readonly IProjectService _projectService;
    private readonly INavigationService _navigation;
    public override string Id => "editor.file.closeProject";
    public override string Description => "Closes the current editor project after pending work is handled.";
    public override I18nKey DisplayNameKey { get; } = new("Command.CloseProject");
    public override I18nKey CategoryKey { get; } = new("Command.Category.File");
    public override Avalonia.Input.KeyGesture? DefaultGesture { get; } = new(Key.W, KeyModifiers.Control);

    public CloseProjectCommand(
        IProjectService projectService,
        INavigationService navigation)
    {
        _projectService = projectService;
        _navigation = navigation;
        InitializeCommand(ExecuteAsync, () => _projectService.Current is not null);
        _projectService.CurrentChanged += _ => RelayCommand.NotifyCanExecuteChanged();
    }

    private async Task ExecuteAsync()
    {
        try
        {
            if (_projectService.Current is null)
                return;
            if (_projectService.Current is { IsDirty: true } project)
                await project.Services.GetRequiredService<EditorWorkspaceViewModel>().SaveAsync();
            await _projectService.CloseAsync();
            if (_navigation.CurrentPage is IDisposable disposable)
                disposable.Dispose();
            _navigation.ResetTo<StartupPageViewModel>();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to close project");
        }
    }
}
