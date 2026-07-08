using GalNet.Core.Services;

namespace GalNet.Control.ViewModels;

public interface IGameFlowFactory
{
    GamePageHostViewModel CreatePageHost(INavigationService parentNavigation, GameFlowOptions? options = null);
    GameStartViewModel CreateStart(INavigationService navigation, GameFlowOptions? options = null);
    GameRunViewModel CreateRun(INavigationService navigation, GameFlowOptions? options = null);
    SettingsViewModel CreateSettings(INavigationService navigation);
}
