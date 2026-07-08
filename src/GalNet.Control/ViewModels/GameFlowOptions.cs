using GalNet.Core.Runtime;
using GalNet.Core.Services;

namespace GalNet.Control.ViewModels;

public sealed class GameFlowOptions
{
    public string Title { get; init; } = "GalNet Demo";
    public string? GameDataDirectory { get; init; }
    public bool UseSampleDataIfMissing { get; init; } = true;

    /// <summary>Optional: override DI-provided variable service.</summary>
    public IVariableService? VariableService { get; init; }

    /// <summary>Optional: override DI-provided game data provider.</summary>
    public IGameDataProvider? GameDataProvider { get; init; }

    public Action<IGameRuntime>? RuntimeCreated { get; init; }
    public Action? GameStarted { get; init; }
}