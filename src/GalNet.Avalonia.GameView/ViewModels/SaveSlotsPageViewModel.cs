using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.Input;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Services;

namespace GalNet.Avalonia.GameView.ViewModels;

public enum SaveSlotsMode { Save, Load }

public sealed partial class SaveSlotsPageViewModel : PageViewModelBase, IActivatablePageViewModel<SaveSlotsMode>, IDisposable
{
    private readonly IGameSessionService _session;
    private readonly IGameNavigationService _navigation;

    public ReadOnlyObservableCollection<GameSaveSlot> SaveSlots => _session.SaveSlots;
    public ObservableCollection<GameSaveSlotRowViewModel> Slots { get; } = [];
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private SaveSlotsMode _mode;
    public string Heading => Mode == SaveSlotsMode.Save ? "Save game" : "Load game";
    public bool IsSaveMode => Mode == SaveSlotsMode.Save;

    public SaveSlotsPageViewModel(IGameSessionService session, IGameNavigationService navigation)
    {
        _session = session;
        _navigation = navigation;
        ((INotifyCollectionChanged)_session.SaveSlots).CollectionChanged += OnSlotsChanged;
        RebuildRows();
    }

    [RelayCommand]
    private async Task SaveSlotAsync(int slotIndex)
    {
        await _session.SaveAsync(slotIndex);
        _navigation.GoBack();
    }

    [RelayCommand]
    private async Task LoadSlotAsync(int slotIndex)
    {
        await _session.LoadAsync(slotIndex);
        _navigation.ResetTo<GamePageViewModel>();
    }

    [RelayCommand] private void Back() => _navigation.GoBack();

    public Task ActivateAsync(SaveSlotsMode mode, CancellationToken cancellationToken = default)
    {
        Mode = mode;
        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(IsSaveMode));
        RebuildRows();
        return Task.CompletedTask;
    }

    public void Dispose() => ((INotifyCollectionChanged)_session.SaveSlots).CollectionChanged -= OnSlotsChanged;
    private void OnSlotsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildRows();
    private void RebuildRows()
    {
        Slots.Clear();
        foreach (var slot in _session.SaveSlots)
        {
            var row = new GameSaveSlotRowViewModel(slot, Mode == SaveSlotsMode.Save, SaveSlotAsync, LoadSlotAsync);
            if (Mode == SaveSlotsMode.Save)
                row.LoadCommand.NotifyCanExecuteChanged();
            Slots.Add(row);
        }
    }
}
