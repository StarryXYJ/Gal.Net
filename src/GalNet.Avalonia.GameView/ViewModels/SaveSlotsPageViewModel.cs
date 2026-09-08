using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.Services;

namespace GalNet.Avalonia.GameView.ViewModels;

public sealed partial class SaveSlotsPageViewModel(IGameSessionService session, IGameNavigationService navigation)
    : PageViewModelBase<SaveSlotsPage>
{
    public ReadOnlyObservableCollection<GameSaveSlot> SaveSlots => session.SaveSlots;

    [RelayCommand] private Task SaveSlotAsync(int slotIndex) => session.SaveAsync(slotIndex);
    [RelayCommand]
    private async Task LoadSlotAsync(int slotIndex)
    {
        await session.LoadAsync(slotIndex);
        navigation.ResetTo<GamePageViewModel>();
    }

    [RelayCommand] private void Back() => navigation.GoBack();
}
