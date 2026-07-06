using System;
using GalNet.Core.Services;

namespace GalNet.Control.ViewModels;

public class GameStartViewModel
{
    public string Title { get; }
    public string[] Buttons { get; }

    private readonly INavigationService _nav;
    private readonly IGameFlowFactory _gameFlowFactory;
    private readonly IGameExitService? _exitService;

    public GameStartViewModel(
        INavigationService nav,
        IGameFlowFactory gameFlowFactory,
        IGameExitService? exitService)
    {
        Title = "GalNet Demo";
        Buttons = ["New Game", "Settings", "Quit"];
        _nav = nav;
        _gameFlowFactory = gameFlowFactory;
        _exitService = exitService;
    }

    public void OnButtonClicked(int index)
    {
        switch (index)
        {
            case 0: // New Game
            {
                _nav.NavigateTo(_gameFlowFactory.CreateRun(_nav));
                break;
            }
            case 1: // Settings
            {
                _nav.NavigateTo(_gameFlowFactory.CreateSettings(_nav));
                break;
            }
            case 2: // Quit
                if (_exitService != null)
                    _exitService.Exit();
                else
                    Environment.Exit(0);
                break;
        }
    }
}
