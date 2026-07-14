using GalNet.Control.ViewModels;
using GalNet.Core.Services;
using GalNet.Core.UI;

namespace GalNet.Control.UI.Instances;

/// <summary>Per-navigation services supplied to a screen template.</summary>
public sealed record ScreenBuildContext(
    INavigationService Navigation,
    IGameFlowFactory GameFlowFactory,
    GameFlowOptions Options,
    object? Parameter = null);

/// <summary>Control-side rendering contract for a DI-registered screen template.</summary>
public interface IScreenBuilderTemplate : IScreenTemplate
{
    object Build(ScreenInstanceDefinition instance, ScreenBuildContext context);
}
