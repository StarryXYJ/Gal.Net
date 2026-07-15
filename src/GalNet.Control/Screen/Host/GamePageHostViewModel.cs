using GalNet.Control.Abstraction.UI;

namespace GalNet.Control.ViewModels;

public sealed class GamePageHostViewModel
{
    public IGameScreenNavigator Navigator { get; }

    public GamePageHostViewModel(IServiceProvider services, IScreenTemplateRegistry templates, GameFlowOptions options)
    {
        GalNet.Control.UI.GameScreenNavigator? navigator = null;
        navigator = new GalNet.Control.UI.GameScreenNavigator(options.Screens, templates,
            parameter => new ScreenBuildContext(services, options.Palette, options.Widgets, options.Screens, navigator!, parameter, options));
        Navigator = navigator;
        _ = Navigator.NavigateAsync("title");
    }
}
