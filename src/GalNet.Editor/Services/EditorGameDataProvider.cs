using System;
using GalNet.Core.Services;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Editor.Services;

/// <summary>
/// Editor implementation of IGameDataProvider.
/// Generates preview data from the current editor workspace state.
/// </summary>
public sealed class EditorGameDataProvider : IGameDataProvider
{
    private readonly IProjectService _projectService;

    public EditorGameDataProvider(IProjectService projectService)
    {
        _projectService = projectService;
    }

    public string DataDirectory => _projectService.Current?.Services
        .GetRequiredService<EditorWorkspaceViewModel>()
        .BuildPreviewData()
        ?? throw new InvalidOperationException("A project must be open before preview data is requested.");
}
