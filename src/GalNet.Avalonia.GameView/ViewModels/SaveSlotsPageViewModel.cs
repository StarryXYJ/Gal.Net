using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.Input;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Services;

namespace GalNet.Avalonia.GameView.ViewModels;

public sealed partial class SaveSlotsPageViewModel : PageViewModelBase, IDisposable
{
    private readonly IGameSessionService _session;
    private readonly IGameNavigationService _navigation;

    public ReadOnlyObservableCollection<GameSaveSlot> SaveSlots => _session.SaveSlots;
    public ObservableCollection<GameSaveSlotRowViewModel> Slots { get; } = [];

    public SaveSlotsPageViewModel(IGameSessionService session, IGameNavigationService navigation)
    {
        _session = session;
        _navigation = navigation;
        ((INotifyCollectionChanged)_session.SaveSlots).CollectionChanged += OnSlotsChanged;
        RebuildRows();
    }

    [RelayCommand] private Task SaveSlotAsync(int slotIndex) => _session.SaveAsync(slotIndex);

    [RelayCommand]
    private async Task LoadSlotAsync(int slotIndex)
    {
        await _session.LoadAsync(slotIndex);
        _navigation.ResetTo<GamePageViewModel>();
    }

    [RelayCommand] private void Back() => _navigation.GoBack();

    public void Dispose() => ((INotifyCollectionChanged)_session.SaveSlots).CollectionChanged -= OnSlotsChanged;
    private void OnSlotsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildRows();
    private void RebuildRows()
    {
        Slots.Clear();
        foreach (var slot in _session.SaveSlots)
            Slots.Add(new GameSaveSlotRowViewModel(slot, SaveSlotAsync, LoadSlotAsync));
    }
}
