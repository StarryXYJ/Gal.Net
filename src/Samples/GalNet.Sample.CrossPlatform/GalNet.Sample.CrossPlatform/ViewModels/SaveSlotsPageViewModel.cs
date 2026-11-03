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
    private GameSaveSlotRowViewModel[] _visibleRows = [];
    public ObservableCollection<GameSaveSlotRowViewModel> Slots => _rows.Slots;
    public PageSlice<GameSaveSlotRowViewModel> Pages { get; }
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
        _rows = new(session, SaveSlotAsync, slot => LoadSlotAsync(slot, CancellationToken.None), loadPreviewsSynchronously: false);
        Pages = new(_rows.Slots, SampleCustomization.PageSize);
        Pages.Changed += LoadVisiblePreviews;
        Pages.UpdateSource(() => _rows.Refresh(IsSaveMode));
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
        Pages.UpdateSource(() => _rows.Refresh(IsSaveMode));
        return Task.CompletedTask;
    }

    private void LoadVisiblePreviews()
    {
        foreach (var row in _visibleRows.Except(Pages.Items)) row.ClearPreview();
        _visibleRows = Pages.Items.ToArray();
        foreach (var row in _visibleRows) _ = row.LoadPreviewAsync();
    }
    protected override void OnBack()
    {
        foreach (var row in _visibleRows) row.ClearPreview();
        _visibleRows = [];
        base.OnBack();
    }
    public void Dispose() { Pages.Changed -= LoadVisiblePreviews; Pages.Dispose(); _rows.Dispose(); }
}
