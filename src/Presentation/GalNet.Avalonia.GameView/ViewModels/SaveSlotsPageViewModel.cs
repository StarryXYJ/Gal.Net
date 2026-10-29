using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.Input;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Services;
using Serilog;

namespace GalNet.Avalonia.GameView.ViewModels;

public enum SaveSlotsMode { Save, Load }

public sealed partial class SaveSlotsPageViewModel : BackNavigablePageViewModel, IActivatablePageViewModel<SaveSlotsMode>, IDisposable
{
    private readonly IGameSessionService _session;
    private readonly GameLaunchFlow _launchFlow;

    public ReadOnlyObservableCollection<GameSaveSlot> SaveSlots => _session.SaveSlots;
    public ObservableCollection<GameSaveSlotRowViewModel> Slots { get; } = [];
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private SaveSlotsMode _mode;
    public string Heading => Mode == SaveSlotsMode.Save ? "Save game" : "Load game";
    public string HeadingKey => Mode == SaveSlotsMode.Save ? "SaveSlots.SaveTitle" : "SaveSlots.LoadTitle";
    public bool IsSaveMode => Mode == SaveSlotsMode.Save;

    public SaveSlotsPageViewModel(
        IGameSessionService session,
        IGameNavigationService navigation,
        GameLaunchFlow launchFlow) : base(navigation)
    {
        _session = session;
        _launchFlow = launchFlow;
        ((INotifyCollectionChanged)_session.SaveSlots).CollectionChanged += OnSlotsChanged;
        RebuildRows();
    }

    [RelayCommand]
    private async Task SaveSlotAsync(int slotIndex)
    {
        await _session.SaveAsync(slotIndex);
        Navigation.GoBack();
    }

    [RelayCommand]
    private async Task LoadSlotAsync(int slotIndex, CancellationToken cancellationToken)
    {
        Func<int, CancellationToken, Task> prepare = _session is IPreparedGameSessionService prepared
            ? prepared.PrepareLoadAsync
            : _session.LoadAsync;
        Func<CancellationToken, Task>? begin = null;
        if (_session is IPreparedGameSessionService preparedSession)
            begin = preparedSession.BeginPreparedGameAsync;

        await _launchFlow.RunAsync(
            token => prepare(slotIndex, token),
            begin,
            cancellationToken);
    }


    public Task ActivateAsync(SaveSlotsMode mode, CancellationToken cancellationToken = default)
    {
        Mode = mode;
        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(HeadingKey));
        OnPropertyChanged(nameof(IsSaveMode));
        RebuildRows();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        ((INotifyCollectionChanged)_session.SaveSlots).CollectionChanged -= OnSlotsChanged;
        foreach (var row in Slots) row.Dispose();
        Slots.Clear();
    }
    private void OnSlotsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Replace && e.NewStartingIndex >= 0 && e.NewItems is { Count: 1 }
            && e.NewItems[0] is GameSaveSlot replacement && e.NewStartingIndex < Slots.Count)
        {
            if (e.OldItems?[0] is GameSaveSlot previous && replacement with { Description = previous.Description } == previous)
                Slots[e.NewStartingIndex].UpdateDescription(replacement.Description);
            else
            {
                var old = Slots[e.NewStartingIndex];
                Slots[e.NewStartingIndex] = CreateRow(replacement);
                old.Dispose();
            }
            return;
        }
        RebuildRows();
    }
    private GameSaveSlotRowViewModel CreateRow(GameSaveSlot slot) => new(
        slot, Mode == SaveSlotsMode.Save, SaveSlotAsync,
        slotIndex => LoadSlotAsync(slotIndex, CancellationToken.None),
        _session is ISaveSlotNamingSession { CanRenameSlots: true } naming
            ? (slotIndex, name) => naming.RenameSlotAsync(slotIndex, name) : null);
    private void RebuildRows()
    {
        foreach (var row in Slots) row.Dispose();
        Slots.Clear();
        foreach (var slot in _session.SaveSlots)
        {
            var row = CreateRow(slot);
            if (Mode == SaveSlotsMode.Save)
                row.LoadCommand.NotifyCanExecuteChanged();
            Slots.Add(row);
        }
    }
}
