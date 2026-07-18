using GalNet.Core.Services;
using GalNet.Control.Abstraction.UI;
using GalNet.Control.UI;
using GalNet.Control.Screen.Host;

namespace GalNet.Control.Screen.Flow;

public interface IGameFlowFactory
{
    GamePageHostViewModel CreatePageHost(INavigationService parentNavigation, GameFlowOptions options);
    object BuildScreen(IGameScreenNavigator navigator, string screen, object? parameter, GameFlowOptions options);
}
