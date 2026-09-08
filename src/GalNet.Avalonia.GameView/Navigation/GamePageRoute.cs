using CommunityToolkit.Mvvm.ComponentModel;

namespace GalNet.Avalonia.GameView.Navigation;

/// <summary>Named routes supplied by the default reusable game shell.</summary>
public enum GamePageRoute
{
    Title,
    Gameplay,
    SaveSlots,
    Settings,
    Gallery,
    About
}

/// <summary>Framework-facing navigation service shared by the player and editor preview hosts.</summary>
public sealed partial class GamePageNavigationService : ObservableObject
{
    private readonly Stack<GamePageRoute> _history = [];

    [ObservableProperty] private GamePageRoute _currentRoute = GamePageRoute.Title;

    public bool IsTitlePage => CurrentRoute == GamePageRoute.Title;
    public bool IsGameplayPage => CurrentRoute == GamePageRoute.Gameplay;
    public bool IsSaveSlotsPage => CurrentRoute == GamePageRoute.SaveSlots;
    public bool IsSettingsPage => CurrentRoute == GamePageRoute.Settings;
    public bool IsGalleryPage => CurrentRoute == GamePageRoute.Gallery;
    public bool IsAboutPage => CurrentRoute == GamePageRoute.About;
    public bool CanGoBack => _history.Count > 0;

    public void Navigate(GamePageRoute route, bool rememberCurrent = true)
    {
        if (route == CurrentRoute) return;
        if (rememberCurrent) _history.Push(CurrentRoute);
        CurrentRoute = route;
    }

    public void ReturnToTitle()
    {
        _history.Clear();
        CurrentRoute = GamePageRoute.Title;
    }

    public void GoBack()
    {
        CurrentRoute = _history.TryPop(out var route) ? route : GamePageRoute.Title;
    }

    partial void OnCurrentRouteChanged(GamePageRoute value)
    {
        OnPropertyChanged(nameof(IsTitlePage));
        OnPropertyChanged(nameof(IsGameplayPage));
        OnPropertyChanged(nameof(IsSaveSlotsPage));
        OnPropertyChanged(nameof(IsSettingsPage));
        OnPropertyChanged(nameof(IsGalleryPage));
        OnPropertyChanged(nameof(IsAboutPage));
        OnPropertyChanged(nameof(CanGoBack));
    }
}
