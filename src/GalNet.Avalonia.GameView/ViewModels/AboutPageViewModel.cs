using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.Services;

namespace GalNet.Avalonia.GameView.ViewModels;

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
