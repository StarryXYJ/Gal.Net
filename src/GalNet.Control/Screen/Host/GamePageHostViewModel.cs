using GalNet.Control.Abstraction.UI;
using GalNet.Control.UI;

namespace GalNet.Control.ViewModels;

public sealed class GamePageHostViewModel
{
    public IGameScreenNavigator Navigator { get; }

    public GamePageHostViewModel(IGameFlowFactory flow, GameFlowOptions options)
    {
        GameScreenNavigator? navigator = null;
        navigator = new GameScreenNavigator((screen, parameter) => flow.BuildScreen(navigator!, screen, parameter, options));
        Navigator = navigator;
        _ = Navigator.NavigateAsync("title");
    }
}
