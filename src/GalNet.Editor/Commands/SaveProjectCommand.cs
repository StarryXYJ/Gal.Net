using System;
using System.Threading.Tasks;
using Avalonia.Input;
using GalNet.Editor.Project;
using GalNet.Editor.Services;
using GalNet.Editor.Shared.Commands;
using Serilog;

namespace GalNet.Editor.Commands;

public class SaveProjectCommand : AsyncEditorCommand
{
    private readonly IProjectService _projectService;

    public SaveProjectCommand(IProjectService projectService, IEditorLocalizationService localization)
    {
        _projectService = projectService;
        Id = "save_project";
        DisplayName = localization["Command.SaveProject"];
        localization.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == "Item[]")
                DisplayName = localization["Command.SaveProject"];
        };
        DefaultGesture = new KeyGesture(Key.S, KeyModifiers.Control);
        InitializeCommand(ExecuteAsync);
    }

    private async Task ExecuteAsync()
    {
        try
        {
            await _projectService.SaveAsync();
            if (_projectService.Current is { } program)
            {
                program.IsDirty = false;
                Log.Information("Project saved: {Name}", program.Name);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save project");
            throw;
        }
    }
}
