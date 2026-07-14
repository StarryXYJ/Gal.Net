using GalNet.Core.Runtime;
using GalNet.Core.Services;
using GalNet.Core.Gallery;
using GalNet.Core.UI;

namespace GalNet.Control.ViewModels;

public sealed record GameFlowOptions
{
    public string Title { get; init; } = "GalNet Demo";
    public string? StartNodeId { get; init; }
    public GameSnapshot? RestoreSnapshot { get; init; }
    public bool IsGalleryPresentation { get; init; }
    public GalleryConfiguration? GalleryConfiguration { get; init; }
    public Action? GameCancelled { get; init; }
    /// <summary>Optional: override DI-provided variable service.</summary>
    public IVariableService? VariableService { get; init; }

    /// <summary>Content is always supplied by the embedding host; previews may build it in memory.</summary>
    public required IGameContentProvider GameContentProvider { get; init; }
    public required IUiProjectProvider UiProjectProvider { get; init; }
    public ISaveService? SaveService { get; init; }
    public IGameProgressService? ProgressService { get; init; }

    public Action<IGameRuntime>? RuntimeCreated { get; init; }
    /// <summary>Raised after a new game or restored save begins executing.</summary>
    public Action? GameStarted { get; init; }
    public Action? GameEnded { get; init; }
    public Action<Exception>? GameFailed { get; init; }
    public Action<GameRunViewModel>? RunCreated { get; init; }
}
