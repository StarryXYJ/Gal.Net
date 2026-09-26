using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Core.Gallery;
using Serilog;

namespace GalNet.Avalonia.GameView.ViewModels;

public sealed partial class TitlePageViewModel : PageViewModelBase, IDisposable
{
    private readonly IGameSessionService _session;
    private readonly IGameNavigationService _navigation;
    private readonly GalleryNavigationService _galleryNavigation;
    private readonly GameLaunchFlow _launchFlow;

    public TitlePageViewModel(
        IGameSessionService session,
        IGameNavigationService navigation,
        GameLaunchFlow launchFlow,
        GalleryNavigationService galleryNavigation)
    {
        _session = session;
        _navigation = navigation;
        _launchFlow = launchFlow;
        _galleryNavigation = galleryNavigation;
        session.PropertyChanged += OnSessionPropertyChanged;
    }

    public string GameTitle => _session.GameTitle;
    public string StatusMessage => _session.StatusMessage;
    public bool IsReady => _session.IsReady;
    public bool CanContinue => _session.CanContinue;
    public bool IsNavigationEnabled => !IsLoading;
    public bool CanStartNewGame => IsReady && !IsLoading;
    public bool CanContinueGame => CanContinue && !IsLoading;
    public bool HasGallery => GetGalleryTypes().Count > 0;
    public bool CanOpenGallery => HasGallery && IsNavigationEnabled;

    [ObservableProperty] private bool _isLoading;

    [RelayCommand]
    private async Task StartNewGameAsync(CancellationToken cancellationToken)
    {
        if (!CanStartNewGame) return;
        IsLoading = true;
        try
        {
            Func<CancellationToken, Task> prepare = _session is IPreparedGameSessionService prepared
                ? prepared.PrepareNewGameAsync
                : _session.StartNewGameAsync;
            await LaunchGameAsync(prepare, cancellationToken);
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task ContinueAsync(CancellationToken cancellationToken)
    {
        if (!CanContinueGame) return;
        IsLoading = true;
        try
        {
            Func<CancellationToken, Task> prepare = _session is IPreparedGameSessionService prepared
                ? prepared.PrepareContinueAsync
                : _session.ContinueAsync;
            await LaunchGameAsync(prepare, cancellationToken);
        }
        finally { IsLoading = false; }
    }

    private Task LaunchGameAsync(Func<CancellationToken, Task> prepare, CancellationToken cancellationToken)
    {
        Func<CancellationToken, Task>? begin = null;
        if (_session is IPreparedGameSessionService prepared)
            begin = prepared.BeginPreparedGameAsync;

        return _launchFlow.RunAsync(prepare, begin, cancellationToken);
    }

    [RelayCommand]
    private Task OpenLoadSlotsAsync(CancellationToken cancellationToken) =>
        _navigation.NavigateAsync<SaveSlotsPageViewModel, SaveSlotsMode>(SaveSlotsMode.Load, cancellationToken);

    [RelayCommand] private void OpenSettings() => _navigation.Navigate<SettingsPageViewModel>();
    [RelayCommand]
    private async Task OpenGalleryAsync(CancellationToken cancellationToken)
    {
        var types = GetGalleryTypes();
        if (types.Count == 0) return;
        if (types.Count == 1)
        {
            await _galleryNavigation.OpenTypeAsync(types[0], cancellationToken);
            return;
        }
        await _navigation.NavigateAsync<GalleryPageViewModel, IReadOnlyList<GalleryTypeData>>(types, cancellationToken);
    }
    [RelayCommand] private void OpenAbout() => _navigation.Navigate<AboutPageViewModel>();

    public void Dispose() => _session.PropertyChanged -= OnSessionPropertyChanged;

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IGameSessionService.GameTitle)) OnPropertyChanged(nameof(GameTitle));
        if (e.PropertyName is nameof(IGameSessionService.StatusMessage)) OnPropertyChanged(nameof(StatusMessage));
        if (e.PropertyName is nameof(IGameSessionService.IsReady))
        {
            OnPropertyChanged(nameof(IsReady));
            OnPropertyChanged(nameof(CanStartNewGame));
        }
        if (e.PropertyName is nameof(IGameSessionService.CanContinue))
        {
            OnPropertyChanged(nameof(CanContinue));
            OnPropertyChanged(nameof(CanContinueGame));
        }
        if (e.PropertyName is nameof(IGameGallerySession.GalleryDataSource))
        {
            OnPropertyChanged(nameof(HasGallery));
            OnPropertyChanged(nameof(CanOpenGallery));
        }
    }

    partial void OnIsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNavigationEnabled));
        OnPropertyChanged(nameof(CanStartNewGame));
        OnPropertyChanged(nameof(CanContinueGame));
        OnPropertyChanged(nameof(CanOpenGallery));
    }

    private IReadOnlyList<GalleryTypeData> GetGalleryTypes() =>
        (_session as IGameGallerySession)?.GalleryDataSource?.GetTypes() ?? [];
}
