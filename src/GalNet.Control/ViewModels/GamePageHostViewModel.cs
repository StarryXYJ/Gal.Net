using GalNet.Core.Services;
using GalNet.Control.Views;

namespace GalNet.Control.ViewModels;

public class GamePageHostViewModel
{
    public INavigationService InternalNav { get; }
    private readonly GameFlowOptions? _options;

    public GamePageHostViewModel(INavigationService parentNav, IGameFlowFactory gameFlowFactory, GameFlowOptions? options = null)
    {
        _options = options;
        InternalNav = parentNav.CreateScope();
        InternalNav.RegisterMap(typeof(GameStartViewModel), typeof(GameStartView));
        InternalNav.RegisterMap(typeof(GameRunViewModel), typeof(GameRunView));
        InternalNav.RegisterMap(typeof(SettingsViewModel), typeof(SettingsView));

        InternalNav.NavigateTo(gameFlowFactory.CreateStart(InternalNav, _options));
    }
}
