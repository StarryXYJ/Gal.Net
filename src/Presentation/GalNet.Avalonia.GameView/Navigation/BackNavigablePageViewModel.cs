using CommunityToolkit.Mvvm.Input;

namespace GalNet.Avalonia.GameView.Navigation;

/// <summary>Opt-in return behavior shared by page buttons, Escape and host navigation.</summary>
public abstract partial class BackNavigablePageViewModel(IGameNavigationService navigation) : PageViewModelBase
{
    protected IGameNavigationService Navigation { get; } = navigation;

    [RelayCommand]
    private void Back() => OnBack();

    /// <summary>Override to clean up page-specific activity before returning.</summary>
    protected virtual void OnBack() => Navigation.GoBack();
}
