using CommunityToolkit.Mvvm.Input;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.ViewModels;

namespace GalNet.Sample.CrossPlatform.ViewModels;

/// <summary>Project menu actions; the runtime owns the composed presentation state.</summary>
public sealed partial class GamePageViewModel(GamePresentationState state, IGameNavigationService navigation)
    : PageViewModelBase, IGameplayPage
{
    public GamePresentationState State { get; } = state;

    [RelayCommand]
    private Task OpenSaveSlotsAsync(CancellationToken cancellationToken) =>
        State.IsGalleryReplay ? Task.CompletedTask : navigation.NavigateAsync<SaveSlotsPageViewModel, SaveSlotsMode>(SaveSlotsMode.Save, cancellationToken);

    [RelayCommand]
    private Task OpenLoadSlotsAsync(CancellationToken cancellationToken) =>
        State.IsGalleryReplay ? Task.CompletedTask : navigation.NavigateAsync<SaveSlotsPageViewModel, SaveSlotsMode>(SaveSlotsMode.Load, cancellationToken);

    [RelayCommand] private void OpenSettings() { if (!State.IsGalleryReplay) navigation.Navigate<SettingsPageViewModel>(); }

    public Task HandleInputActionAsync(string action)
    {
        switch (action)
        {
            case "open.save": return OpenSaveSlotsCommand.ExecuteAsync(null);
            case "open.load": return OpenLoadSlotsCommand.ExecuteAsync(null);
            case "open.settings": OpenSettingsCommand.Execute(null); break;
        }
        return Task.CompletedTask;
    }
}
