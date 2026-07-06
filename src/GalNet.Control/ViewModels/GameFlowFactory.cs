using GalNet.Control.View;
using GalNet.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Control.ViewModels;

public sealed class GameFlowFactory : IGameFlowFactory
{
    private readonly IServiceProvider _serviceProvider;

    public GameFlowFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public GamePageHostViewModel CreatePageHost(INavigationService parentNavigation) =>
        new(parentNavigation, this);

    public GameStartViewModel CreateStart(INavigationService navigation) =>
        new(navigation, this, _serviceProvider.GetService<IGameExitService>());

    public GameRunViewModel CreateRun(INavigationService navigation)
    {
        var gameView = _serviceProvider.GetRequiredService<DefaultGameView>();
        var settings = _serviceProvider.GetRequiredService<ISettingsService>();

        return new GameRunViewModel(gameView, settings, () =>
        {
            navigation.Clear();
            navigation.NavigateTo(CreateStart(navigation));
        });
    }

    public SettingsViewModel CreateSettings(INavigationService navigation) =>
        new(_serviceProvider.GetRequiredService<ISettingsService>(), navigation);
}
