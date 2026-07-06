using GalNet.Core.Services;
using GalNet.Control.Views;

namespace GalNet.Control.ViewModels;

public class GamePageHostViewModel
{
    public INavigationService InternalNav { get; }

    public GamePageHostViewModel(INavigationService parentNav, IServiceProvider serviceProvider)
    {
        InternalNav = parentNav.CreateScope();
        InternalNav.RegisterMap(typeof(GameStartViewModel), typeof(GameStartView));
        InternalNav.RegisterMap(typeof(GameRunViewModel), typeof(GameRunView));
        InternalNav.RegisterMap(typeof(SettingsViewModel), typeof(SettingsView));

        var startVm = new GameStartViewModel(InternalNav, serviceProvider);
        InternalNav.NavigateTo(startVm);
    }
}
