using CommunityToolkit.Mvvm.Input;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Page;

namespace GalNet.Avalonia.GameView.ViewModels;

public sealed partial class GalleryPageViewModel(IGameNavigationService navigation) : PageViewModelBase<GalleryPage>
{
    [RelayCommand] private void Back() => navigation.GoBack();
}
