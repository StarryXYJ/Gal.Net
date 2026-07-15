using GalNet.Core.Services;
using GalNet.Control.Abstraction.UI;
using GalNet.Control.UI;

namespace GalNet.Control.ViewModels;

public interface IGameFlowFactory
{
    GamePageHostViewModel CreatePageHost(INavigationService parentNavigation, GameFlowOptions options);
    GameStartViewModel CreateStart(IGameScreenNavigator navigator, GameFlowOptions options);
    GameRunViewModel CreateRun(IGameScreenNavigator navigator, GameFlowOptions options, WidgetBuildContext widgetContext, GameScreenConfiguration config);
    SettingsViewModel CreateSettings(IGameScreenNavigator navigator);
    SaveLoadViewModel CreateSaveLoad(IGameScreenNavigator navigator, GameFlowOptions options, SaveLoadMode mode, Func<int, Task>? load = null, Func<int, Task>? save = null);
    GalleryViewModel CreateGallery(IGameScreenNavigator navigator, GameFlowOptions options);
}
