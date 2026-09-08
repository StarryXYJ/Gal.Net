using CommunityToolkit.Mvvm.Input;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Page;

namespace GalNet.Avalonia.GameView.ViewModels;

public sealed partial class SettingsPageViewModel(GamePageViewModel gameplay, IGameNavigationService navigation)
    : PageViewModelBase<SettingsPage>
{
    public double TextSpeed { get => gameplay.TextSpeed; set => gameplay.TextSpeed = value; }
    [RelayCommand] private void Back() => navigation.GoBack();
}
