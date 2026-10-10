using CommunityToolkit.Mvvm.Input;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Services;

namespace GalNet.Avalonia.GameView.ViewModels;

public sealed partial class SettingsPageViewModel(IGameNavigationService navigation,
    IPlayerSettingsService settings)
    : PageViewModelBase
{
    public IReadOnlyList<PlayerSetting> Items => settings.Items;
    [RelayCommand] private void Back() => navigation.GoBack();
}
