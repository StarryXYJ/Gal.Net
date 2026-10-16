using System.ComponentModel;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Services;

namespace GalNet.Avalonia.GameView.ViewModels;

public sealed partial class AboutPageViewModel : BackNavigablePageViewModel, IDisposable
{
    private readonly IGameSessionService _session;

    public AboutPageViewModel(IGameSessionService session, IGameNavigationService navigation) : base(navigation)
    {
        _session = session;
        session.PropertyChanged += OnSessionPropertyChanged;
    }

    public string GameTitle => _session.GameTitle;
    public void Dispose() => _session.PropertyChanged -= OnSessionPropertyChanged;
    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IGameSessionService.GameTitle)) OnPropertyChanged(nameof(GameTitle));
    }
}
