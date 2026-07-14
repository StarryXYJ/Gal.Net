using System;
using GalNet.Core.Services;

namespace GalNet.Control.ViewModels;

public class GameStartViewModel
{
    public string Title { get; }
    public string[] Buttons { get; private set; }
    public event Action? ButtonsChanged;

    private readonly INavigationService _nav;
    private readonly IGameFlowFactory _gameFlowFactory;
    private readonly IGameExitService? _exitService;
    private readonly GameFlowOptions? _options;
    private readonly ISaveService? _saves;

    public GameStartViewModel(
        INavigationService nav,
        IGameFlowFactory gameFlowFactory,
        IGameExitService? exitService,
        GameFlowOptions? options = null, ISaveService? saves = null)
    {
        Title = options?.Title ?? "GalNet Demo";
        _saves = saves;
        Buttons = ["New Game", "Load", "Gallery", "Settings", "Quit"];
        _nav = nav;
        _gameFlowFactory = gameFlowFactory;
        _exitService = exitService;
        _options = options;
        _ = RefreshContinueAsync();
    }

    private async Task RefreshContinueAsync()
    {
        if (_saves is null || !await _saves.HasQuickSaveAsync().ConfigureAwait(false)) return;
        Buttons = ["Continue", "New Game", "Load", "Gallery", "Settings", "Quit"];
        Avalonia.Threading.Dispatcher.UIThread.Post(() => ButtonsChanged?.Invoke());
    }

    public void OnButtonClicked(int index)
    {
        var button = Buttons[index];
        switch (button)
        {
            case "Continue":
                _ = ContinueAsync();
                break;
            case "New Game":
            {
                _nav.NavigateTo(_gameFlowFactory.CreateRun(_nav, _options));
                break;
            }
            case "Load":
                _nav.NavigateTo(_gameFlowFactory.CreateSaveLoad(_nav, _options, SaveLoadMode.Load, LoadAsync));
                break;
            case "Gallery":
                _nav.NavigateTo(_gameFlowFactory.CreateGallery(_nav, _options));
                break;
            case "Settings":
            {
                _nav.NavigateTo(_gameFlowFactory.CreateSettings(_nav));
                break;
            }
            case "Quit":
                if (_exitService != null)
                    _exitService.Exit();
                else
                    Environment.Exit(0);
                break;
        }
    }

    private async Task ContinueAsync()
    {
        var snapshot = _saves is null ? null : await _saves.QuickLoadAsync();
        if (snapshot is not null) _nav.NavigateTo(_gameFlowFactory.CreateRun(_nav, _options with { RestoreSnapshot = snapshot }));
    }

    private async Task LoadAsync(int slot)
    {
        var snapshot = _saves is null ? null : await _saves.LoadAsync(slot);
        if (snapshot is not null) _nav.NavigateTo(_gameFlowFactory.CreateRun(_nav, _options with { RestoreSnapshot = snapshot }));
    }
}
