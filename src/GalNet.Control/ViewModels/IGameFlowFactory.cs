using GalNet.Core.Services;

namespace GalNet.Control.ViewModels;

public interface IGameFlowFactory
{
    GamePageHostViewModel CreatePageHost(INavigationService parentNavigation);
    GameStartViewModel CreateStart(INavigationService navigation);
    GameRunViewModel CreateRun(INavigationService navigation);
    SettingsViewModel CreateSettings(INavigationService navigation);
}
