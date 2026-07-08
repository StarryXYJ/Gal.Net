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

    public GamePageHostViewModel CreatePageHost(INavigationService parentNavigation, GameFlowOptions? options = null) =>
        new(parentNavigation, this, options);

    public GameStartViewModel CreateStart(INavigationService navigation, GameFlowOptions? options = null) =>
        new(navigation, this, _serviceProvider.GetService<IGameExitService>(), options);

    public GameRunViewModel CreateRun(INavigationService navigation, GameFlowOptions? options = null)
    {
        var gameView = _serviceProvider.GetRequiredService<DefaultGameView>();
        var settings = _serviceProvider.GetRequiredService<ISettingsService>();
        var variableService = options?.VariableService ?? _serviceProvider.GetService<IVariableService>();
        var gameDataProvider = options?.GameDataProvider ?? _serviceProvider.GetService<IGameDataProvider>();

        return new GameRunViewModel(gameView, settings, variableService, gameDataProvider, options, () =>
        {
            navigation.Clear();
            navigation.NavigateTo(CreateStart(navigation, options));
        });
    }

    public SettingsViewModel CreateSettings(INavigationService navigation) =>
        new(_serviceProvider.GetRequiredService<ISettingsService>(), navigation);
}