using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Avalonia.GameView.Navigation;

namespace GalNet.Avalonia.GameView.Page;

/// <summary>State and commands for the default page shell. Hosts provide gameplay and persistence services.</summary>
public sealed partial class GameShellViewModel : ObservableObject
{
    public GameShellViewModel()
    {
        Game = new GamePageViewModel(Navigation);
    }

    public GamePageNavigationService Navigation { get; } = new();
    public GamePageViewModel Game { get; }

    [ObservableProperty] private string _gameTitle = "GalNet Game";
    [ObservableProperty] private string _statusMessage = "Load a game to begin.";
    [ObservableProperty] private bool _isReady;

    public event Action? StartGameRequested;

    [RelayCommand]
    private void StartGame()
    {
        if (!IsReady) return;
        Navigation.Navigate(GamePageRoute.Gameplay, rememberCurrent: false);
        StartGameRequested?.Invoke();
    }

    [RelayCommand] private void OpenSettings() => Navigation.Navigate(GamePageRoute.Settings);
    [RelayCommand] private void OpenAbout() => Navigation.Navigate(GamePageRoute.About);
    [RelayCommand] private void Back() => Navigation.GoBack();
    [RelayCommand] private void ReturnToTitle() => Navigation.ReturnToTitle();
}
