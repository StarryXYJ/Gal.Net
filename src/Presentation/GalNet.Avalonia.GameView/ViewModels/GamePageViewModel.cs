using CommunityToolkit.Mvvm.Input;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Presentation;

namespace GalNet.Avalonia.GameView.ViewModels;

/// <summary>Temporary default-page adapter; application routes move to Sample in P3/P4.</summary>
public partial class GamePageViewModel(IGameNavigationService navigation) : GamePresentationState, IGameplayPage
{
    public GamePresentationState State => this;
    private readonly IGameNavigationService _navigation = navigation;
    [RelayCommand]
    private Task OpenSaveSlotsAsync(CancellationToken cancellationToken) =>
        IsGalleryReplay ? Task.CompletedTask : _navigation.NavigateAsync<SaveSlotsPageViewModel, SaveSlotsMode>(SaveSlotsMode.Save, cancellationToken);

    [RelayCommand]
    private Task OpenLoadSlotsAsync(CancellationToken cancellationToken) =>
        IsGalleryReplay ? Task.CompletedTask : _navigation.NavigateAsync<SaveSlotsPageViewModel, SaveSlotsMode>(SaveSlotsMode.Load, cancellationToken);

    [RelayCommand] private void OpenSettings() { if (!IsGalleryReplay) _navigation.Navigate<SettingsPageViewModel>(); }
    [RelayCommand] private void ReturnToGame() => _navigation.GoBack();
}
