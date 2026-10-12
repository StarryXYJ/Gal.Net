using CommunityToolkit.Mvvm.Input;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.Settings;

namespace GalNet.Avalonia.GameView.ViewModels;

public sealed partial class SettingsPageViewModel(IGameNavigationService navigation,
    IPlayerSettingsService settings)
    : PageViewModelBase
{
    public IReadOnlyList<SettingCategory> Categories => settings.Categories;
    [RelayCommand] private void Back() => navigation.GoBack();
}
