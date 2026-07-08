using GalNet.Core.Services;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Services;

/// <summary>
/// Editor implementation of IGameDataProvider.
/// Generates preview data from the current editor workspace state.
/// </summary>
public sealed class EditorGameDataProvider : IGameDataProvider
{
    private readonly EditorWorkspaceViewModel _workspace;

    public EditorGameDataProvider(EditorWorkspaceViewModel workspace)
    {
        _workspace = workspace;
    }

    public string DataDirectory => _workspace.BuildPreviewData();
}