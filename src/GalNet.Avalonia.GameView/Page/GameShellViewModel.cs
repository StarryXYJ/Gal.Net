using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Avalonia.GameView.Navigation;

namespace GalNet.Avalonia.GameView.Page;

/// <summary>Scope-owned navigation host for the default game pages.</summary>
public sealed class GameShellViewModel : ObservableObject, IDisposable
{
    private readonly IGameNavigationService _navigation;

    public GameShellViewModel(IGameNavigationService navigation)
    {
        _navigation = navigation;
        _navigation.CurrentViewModelChanged += OnCurrentViewModelChanged;
    }

    public PageViewModelBase? CurrentViewModel => _navigation.CurrentViewModel;

    public void Dispose() => _navigation.CurrentViewModelChanged -= OnCurrentViewModelChanged;

    private void OnCurrentViewModelChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(CurrentViewModel));
}
