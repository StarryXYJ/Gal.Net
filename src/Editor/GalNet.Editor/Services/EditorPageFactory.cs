using System;
using GalNet.Editor.Services.Interfaces;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Editor.Services;

public sealed class EditorPageFactory : IEditorPageFactory
{
    private readonly IProjectService _projects;

    public EditorPageFactory(IProjectService projects)
    {
        _projects = projects;
    }

    public EditorPageViewModel CreateEditorPage() =>
        (_projects.Current ?? throw new InvalidOperationException("An editor page requires an open project."))
        .Services.GetRequiredService<EditorPageViewModel>();
}
