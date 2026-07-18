using GalNet.Core.Runtime;
using GalNet.Core.Services;
using GalNet.Core.Gallery;
using GalNet.Core.UI;
using GalNet.Core.Assets;
using GalNet.Control.Screen.Game;

namespace GalNet.Control.Screen.Flow;

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
    /// <summary>One host-owned document configuring the fixed built-in screens.</summary>
    public required UiProject Ui { get; init; }
    /// <summary>Host-provided resource lookup used by UI pages for ID-backed visual assets.</summary>
    public IAssetManager? AssetManager { get; init; }
    public ISaveService? SaveService { get; init; }
    public IGameProgressService? ProgressService { get; init; }

    public Action<IGameRuntime>? RuntimeCreated { get; init; }
    /// <summary>Raised after a new game or restored save begins executing.</summary>
    public Action? GameStarted { get; init; }
    public Action? GameEnded { get; init; }
    public Action<Exception>? GameFailed { get; init; }
    public Action<GameRunViewModel>? RunCreated { get; init; }
}
