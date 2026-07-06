using System;
using System.Threading.Tasks;
using GalNet.Core.Services;
using GalNet.Editor.Project;
using GalNet.Editor.Shared.Commands;
using Serilog;

namespace GalNet.Editor.Commands;

/// <summary>关闭当前项目，返回启动页</summary>
public class CloseProjectCommand : AsyncEditorCommand
{
    private readonly IProjectService _projectService;
    private readonly INavigationService _navigation;

    public CloseProjectCommand(IProjectService projectService, INavigationService navigation)
    {
        _projectService = projectService;
        _navigation = navigation;
        Id = "close_project";
        DisplayName = "关闭项目";
        _command = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(ExecuteAsync);
    }

    private async Task ExecuteAsync()
    {
        try
        {
            await _projectService.CloseAsync();
            _navigation.GoBack();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to close project");
        }
    }
}
