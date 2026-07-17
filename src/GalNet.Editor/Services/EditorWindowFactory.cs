using System;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Views;
using GalNet.Editor.Abstraction.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Editor.Services;

public sealed class EditorWindowFactory : IEditorWindowFactory
{
    private readonly IProjectService _projects;

    public EditorWindowFactory(IProjectService projects)
    {
        _projects = projects;
    }

    private IServiceProvider ProjectServices =>
        (_projects.Current ?? throw new InvalidOperationException("An editor window requires an open project."))
        .Services;

    public ProjectSettingsWindow CreateProjectSettingsWindow()
    {
        var window = ProjectServices.GetRequiredService<ProjectSettingsWindow>();
        window.DataContext = ProjectServices.GetRequiredService<ProjectSettingsPanelViewModel>();
        return window;
    }

    public EditorSettingsWindow CreateEditorSettingsWindow()
    {
        var window = ProjectServices.GetRequiredService<EditorSettingsWindow>();
        window.DataContext = ProjectServices.GetRequiredService<EditorSettingsPanelViewModel>();
        return window;
    }

    public ExportWindow CreateExportWindow()
    {
        var window = ProjectServices.GetRequiredService<ExportWindow>();
        window.DataContext = ProjectServices.GetRequiredService<ExportPanelViewModel>();
        return window;
    }
}
