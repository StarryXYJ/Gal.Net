using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GalNet.Core.Services;
using GalNet.Control.UI;
using GalNet.Runtime.Loader;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Editor.Services;

/// <summary>
/// Editor implementation of IGameDataProvider.
/// Generates preview data from the current editor workspace state.
/// </summary>
public sealed class EditorGameDataProvider : IGameContentProvider
{
    private readonly IProjectService _projectService;

    public EditorGameDataProvider(IProjectService projectService)
    {
        _projectService = projectService;
    }

    public Task<GameContent> LoadAsync(CancellationToken cancellationToken = default)
    {
        var project = _projectService.Current ?? throw new InvalidOperationException("A project must be open before preview data is requested.");
        var directory = project.Services.GetRequiredService<EditorWorkspaceViewModel>().BuildPreviewData();
        var graph = GraphLoader.LoadFromFile(Path.Combine(directory, "graph.json"));
        foreach (var group in graph.Nodes.OfType<GalNet.Core.Graph.Group>())
        {
            var file = Path.Combine(directory, $"{group.Id}.galgroup");
            if (File.Exists(file)) GalgroupLoader.LoadIntoGroup(group, file);
        }
        var ui = project.UiProject.Current;
        return Task.FromResult(new GameContent { Graph = graph, Ui = ui, AssetRoot = project.AssetsPath });
    }
}
