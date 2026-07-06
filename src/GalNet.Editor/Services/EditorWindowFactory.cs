using System;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Editor.Services;

public sealed class EditorWindowFactory : IEditorWindowFactory
{
    private readonly IServiceProvider _serviceProvider;

    public EditorWindowFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public ProjectSettingsWindow CreateProjectSettingsWindow()
    {
        var window = _serviceProvider.GetRequiredService<ProjectSettingsWindow>();
        window.DataContext = _serviceProvider.GetRequiredService<ProjectSettingsPanelViewModel>();
        return window;
    }

    public EditorSettingsWindow CreateEditorSettingsWindow()
    {
        var window = _serviceProvider.GetRequiredService<EditorSettingsWindow>();
        window.DataContext = _serviceProvider.GetRequiredService<EditorSettingsPanelViewModel>();
        return window;
    }
}
