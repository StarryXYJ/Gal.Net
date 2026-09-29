using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Avalonia.GameView.Navigation;

namespace GalNet.Avalonia.GameView.ViewModels;

/// <summary>
/// The default loading overlay model. Hosts can replace its view mapping during composition.
/// </summary>
public sealed partial class LoadingPageViewModel : PageViewModelBase
{
    [ObservableProperty] private string _message = "Loading...";
}
