using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Control.Abstraction.UI;
using GalNet.Core.Services;
using GalNet.Core.UI;

namespace GalNet.Control.ViewModels;

public sealed partial class GameStartViewModel : ObservableObject
{
    private readonly IGameScreenNavigator _navigator; private readonly IGameExitService? _exit; private readonly ISaveService? _saves;
    public string Title { get; }
    public TitleUiConfiguration Configuration { get; }
    [ObservableProperty] private bool _isContinueVisible;
    public bool ShowGallery => Configuration.ShowGallery;
    public GameStartViewModel(IGameScreenNavigator navigator, IGameExitService? exit, GameFlowOptions options, ISaveService? saves, TitleUiConfiguration config)
    { _navigator = navigator; _exit = exit; _saves = saves; Configuration = config; Title = options.Title; _ = RefreshContinueAsync(); }
    [RelayCommand] private async Task ContinueAsync() { var snapshot = _saves is null ? null : await _saves.QuickLoadAsync(); if (snapshot is not null) await _navigator.NavigateAsync("game", snapshot); }
    [RelayCommand] private Task NewGameAsync() => _navigator.NavigateAsync("game");
    [RelayCommand] private Task LoadAsync() => _navigator.NavigateAsync("save-load");
    [RelayCommand] private Task GalleryAsync() => _navigator.NavigateAsync("gallery");
    [RelayCommand] private Task SettingsAsync() => _navigator.NavigateAsync("settings");
    [RelayCommand] private void Quit() { if (_exit is not null) _exit.Exit(); else Environment.Exit(0); }
    private async Task RefreshContinueAsync() { if (_saves is not null && await _saves.HasQuickSaveAsync()) Avalonia.Threading.Dispatcher.UIThread.Post(() => IsContinueVisible = true); }
}
