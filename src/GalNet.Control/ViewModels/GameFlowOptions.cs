using GalNet.Core.Runtime;
using GalNet.Core.Services;
using GalNet.Core.Gallery;

namespace GalNet.Control.ViewModels;

public sealed record GameFlowOptions
{
    public string Title { get; init; } = "GalNet Demo";
    public string? GameDataDirectory { get; init; }
    /// <summary>Per-user, per-game directory supplied by the launcher.</summary>
    public string? ProfileDirectory { get; init; }
    public int SaveSlotCount { get; init; } = 60;
    public string? StartNodeId { get; init; }
    public GameSnapshot? RestoreSnapshot { get; init; }
    public bool IsGalleryPresentation { get; init; }
    public GalleryConfiguration? GalleryConfiguration { get; init; }
    public Action? GameCancelled { get; init; }
    public bool UseSampleDataIfMissing { get; init; } = true;

    /// <summary>Optional: override DI-provided variable service.</summary>
    public IVariableService? VariableService { get; init; }

    /// <summary>Optional: override DI-provided game data provider.</summary>
    public IGameDataProvider? GameDataProvider { get; init; }

    public Action<IGameRuntime>? RuntimeCreated { get; init; }
    /// <summary>Raised after a new game or restored save begins executing.</summary>
    public Action? GameStarted { get; init; }
    public Action? GameEnded { get; init; }
    public Action<Exception>? GameFailed { get; init; }
    public Action<GameRunViewModel>? RunCreated { get; init; }
}
