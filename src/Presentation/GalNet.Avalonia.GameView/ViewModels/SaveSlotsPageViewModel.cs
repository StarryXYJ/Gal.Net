using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.Input;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Services;
using Serilog;

namespace GalNet.Avalonia.GameView.ViewModels;

public enum SaveSlotsMode { Save, Load }

public sealed partial class SaveSlotsPageViewModel : PageViewModelBase, IActivatablePageViewModel<SaveSlotsMode>, IDisposable
{
    private readonly IGameSessionService _session;
    private readonly IGameNavigationService _navigation;
    private readonly GameLaunchFlow _launchFlow;

    public ReadOnlyObservableCollection<GameSaveSlot> SaveSlots => _session.SaveSlots;
    public ObservableCollection<GameSaveSlotRowViewModel> Slots { get; } = [];
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private SaveSlotsMode _mode;
    public string Heading => Mode == SaveSlotsMode.Save ? "Save game" : "Load game";
    public bool IsSaveMode => Mode == SaveSlotsMode.Save;

    public SaveSlotsPageViewModel(
        IGameSessionService session,
        IGameNavigationService navigation,
        GameLaunchFlow launchFlow)
    {
        _session = session;
        _navigation = navigation;
        _launchFlow = launchFlow;
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
            var row = new GameSaveSlotRowViewModel(
                slot,
                Mode == SaveSlotsMode.Save,
                SaveSlotAsync,
                slotIndex => LoadSlotAsync(slotIndex, CancellationToken.None));
            if (Mode == SaveSlotsMode.Save)
                row.LoadCommand.NotifyCanExecuteChanged();
            Slots.Add(row);
        }
    }
}
