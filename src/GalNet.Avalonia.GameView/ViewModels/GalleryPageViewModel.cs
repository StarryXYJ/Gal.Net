using CommunityToolkit.Mvvm.Input;
using GalNet.Avalonia.GameView.Navigation;

namespace GalNet.Avalonia.GameView.ViewModels;

public sealed partial class GalleryPageViewModel(IGameNavigationService navigation) : PageViewModelBase
{
    [RelayCommand] private void Back() => navigation.GoBack();
}
