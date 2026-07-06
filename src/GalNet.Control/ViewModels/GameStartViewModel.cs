using System;
using GalNet.Control.View;
using GalNet.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Control.ViewModels;

public class GameStartViewModel
{
    public string Title { get; }
    public string[] Buttons { get; }

    private readonly INavigationService _nav;
    private readonly IServiceProvider _sp;

    public GameStartViewModel(INavigationService nav, IServiceProvider serviceProvider)
    {
        Title = "GalNet Demo";
        Buttons = ["New Game", "Settings", "Quit"];
        _nav = nav;
        _sp = serviceProvider;
    }

    public void OnButtonClicked(int index)
    {
        switch (index)
        {
            case 0: // New Game
            {
                var gameView = _sp.GetRequiredService<DefaultGameView>();
                var settings = _sp.GetRequiredService<ISettingsService>();
                var runVm = new GameRunViewModel(gameView, settings, () =>
                {
                    _nav.Clear();
                    _nav.NavigateTo(new GameStartViewModel(_nav, _sp));
                });
                _nav.NavigateTo(runVm);
                break;
            }
            case 1: // Settings
            {
                var settingsVm = new SettingsViewModel(_sp.GetRequiredService<ISettingsService>(), _nav);
                _nav.NavigateTo(settingsVm);
                break;
            }
            case 2: // Quit
                // Resolve from DI, fall back to Environment.Exit
                var exitService = _sp.GetService<IGameExitService>();
                if (exitService != null)
                    exitService.Exit();
                else
                    Environment.Exit(0);
                break;
        }
    }
}
