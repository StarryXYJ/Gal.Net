using System;
using System.Threading.Tasks;
using Avalonia.Input;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.ViewModels;
using GalNet.Core.I18n;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace GalNet.Editor.Commands;

public sealed class SaveProjectCommand : AsyncEditorUiCommand
{
    private readonly IProjectService _projectService;
    public override string Id => "editor.file.saveProject";
    public override string Description => "Saves the current editor document and project settings.";
    public override I18nKey DisplayNameKey { get; } = new("Command.SaveProject");
    public override I18nKey CategoryKey { get; } = new("Command.Category.File");
    public override KeyGesture? DefaultGesture { get; } = new(Key.S, KeyModifiers.Control);

    public SaveProjectCommand(IProjectService projectService)
    {
        _projectService = projectService;
        InitializeCommand(ExecuteAsync);
    }

    private async Task ExecuteAsync()
    {
        try
        {
            if (_projectService.Current is { } project)
                await project.Services.GetRequiredService<EditorWorkspaceViewModel>().SaveAsync();
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
