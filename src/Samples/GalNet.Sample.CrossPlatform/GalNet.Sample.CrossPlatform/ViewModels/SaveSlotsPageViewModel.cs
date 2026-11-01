using GalNet.Presentation.Abstractions.Application;
using System.Collections.ObjectModel;
using GalNet.Avalonia.GameView.Presentation;
using CommunityToolkit.Mvvm.Input;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.ViewModels;

namespace GalNet.Sample.CrossPlatform.ViewModels;


public sealed partial class SaveSlotsPageViewModel : BackNavigablePageViewModel, IActivatablePageViewModel<SaveSlotsMode>, IGameSavePreviewPage, IDisposable
{
    private readonly IGameSessionService _session;
    private readonly GameLaunchFlow _launchFlow;

    public ReadOnlyObservableCollection<GameSaveSlot> SaveSlots => _session.SaveSlots;
    private readonly SaveSlotCollection _rows;
    public ObservableCollection<GameSaveSlotRowViewModel> Slots => _rows.Slots;
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
        _rows = new(session, SaveSlotAsync, slot => LoadSlotAsync(slot, CancellationToken.None));
        _rows.Refresh(IsSaveMode);
    }

    [RelayCommand]
    private async Task SaveSlotAsync(int slotIndex)
    {
        await _session.SaveAsync(slotIndex);
        Navigation.GoBack();
    }

    [RelayCommand]
    private Task LoadSlotAsync(int slotIndex, CancellationToken cancellationToken) =>
        _launchFlow.LoadAsync(_session, slotIndex, cancellationToken);


    public Task ActivateAsync(SaveSlotsMode mode, CancellationToken cancellationToken = default)
    {
        Mode = mode;
        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(HeadingKey));
        OnPropertyChanged(nameof(IsSaveMode));
        _rows.Refresh(IsSaveMode);
        return Task.CompletedTask;
    }

    public void Dispose() => _rows.Dispose();
}
