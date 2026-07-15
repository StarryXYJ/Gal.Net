using GalNet.Core.Services;
using GalNet.Control.Abstraction.UI;
using GalNet.Control.UI;

namespace GalNet.Control.ViewModels;

public interface IGameFlowFactory
{
    GamePageHostViewModel CreatePageHost(INavigationService parentNavigation, GameFlowOptions options);
    object BuildScreen(IGameScreenNavigator navigator, string screen, object? parameter, GameFlowOptions options);
}
