using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Avalonia.GameView.Navigation;

namespace GalNet.Avalonia.GameView.Page;

public sealed partial class TitlePageViewModel : PageViewModelBase<TitlePage>, IDisposable
{
    private readonly IGameSessionService _session;
    private readonly IGameNavigationService _navigation;

    public TitlePageViewModel(IGameSessionService session, IGameNavigationService navigation)
    {
        _session = session;
        _navigation = navigation;
        session.PropertyChanged += OnSessionPropertyChanged;
    }

    public string GameTitle => _session.GameTitle;
    public string StatusMessage => _session.StatusMessage;
    public bool IsReady => _session.IsReady;

    [RelayCommand]
    private async Task StartGameAsync(CancellationToken cancellationToken)
    {
        if (!IsReady) return;
        _navigation.ResetTo<GamePageViewModel>();
        await _session.StartAsync(cancellationToken);
    }

    [RelayCommand] private void OpenSettings() => _navigation.Navigate<SettingsPageViewModel>();
    [RelayCommand] private void OpenAbout() => _navigation.Navigate<AboutPageViewModel>();

    public void Dispose() => _session.PropertyChanged -= OnSessionPropertyChanged;

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IGameSessionService.GameTitle)) OnPropertyChanged(nameof(GameTitle));
        if (e.PropertyName is nameof(IGameSessionService.StatusMessage)) OnPropertyChanged(nameof(StatusMessage));
        if (e.PropertyName is nameof(IGameSessionService.IsReady)) OnPropertyChanged(nameof(IsReady));
    }
}

public sealed partial class SaveSlotsPageViewModel(IGameSessionService session, IGameNavigationService navigation)
    : PageViewModelBase<SaveSlotsPage>
{
    public System.Collections.ObjectModel.ReadOnlyObservableCollection<GameSaveSlot> SaveSlots => session.SaveSlots;

    [RelayCommand] private Task SaveSlotAsync(int slotIndex) => session.SaveAsync(slotIndex);
    [RelayCommand]
    private async Task LoadSlotAsync(int slotIndex)
    {
        await session.LoadAsync(slotIndex);
        navigation.ResetTo<GamePageViewModel>();
    }

    [RelayCommand] private void Back() => navigation.GoBack();
}

public sealed partial class SettingsPageViewModel(GamePageViewModel gameplay, IGameNavigationService navigation)
    : PageViewModelBase<SettingsPage>
{
    public double TextSpeed
    {
        get => gameplay.TextSpeed;
        set => gameplay.TextSpeed = value;
    }

    [RelayCommand] private void Back() => navigation.GoBack();
}

public sealed partial class GalleryPageViewModel(IGameNavigationService navigation) : PageViewModelBase<GalleryPage>
{
    [RelayCommand] private void Back() => navigation.GoBack();
}

public sealed partial class AboutPageViewModel : PageViewModelBase<AboutPage>, IDisposable
{
    private readonly IGameSessionService _session;
    private readonly IGameNavigationService _navigation;

    public AboutPageViewModel(IGameSessionService session, IGameNavigationService navigation)
    {
        _session = session;
        _navigation = navigation;
        session.PropertyChanged += OnSessionPropertyChanged;
    }

    public string GameTitle => _session.GameTitle;
    [RelayCommand] private void Back() => _navigation.GoBack();

    public void Dispose() => _session.PropertyChanged -= OnSessionPropertyChanged;

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IGameSessionService.GameTitle)) OnPropertyChanged(nameof(GameTitle));
    }
}
