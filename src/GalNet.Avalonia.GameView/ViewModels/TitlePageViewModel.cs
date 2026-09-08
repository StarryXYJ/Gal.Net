using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Services;

namespace GalNet.Avalonia.GameView.ViewModels;

public sealed partial class TitlePageViewModel : PageViewModelBase, IDisposable
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
