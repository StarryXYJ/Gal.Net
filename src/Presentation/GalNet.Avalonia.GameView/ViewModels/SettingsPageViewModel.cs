using CommunityToolkit.Mvvm.Input;
using GalNet.Avalonia.GameView.Navigation;

namespace GalNet.Avalonia.GameView.ViewModels;

public sealed partial class SettingsPageViewModel(GamePageViewModel gameplay, IGameNavigationService navigation)
    : PageViewModelBase
{
    public double TextSpeed { get => gameplay.TextSpeed; set => gameplay.TextSpeed = value; }
    [RelayCommand] private void Back() => navigation.GoBack();
}
