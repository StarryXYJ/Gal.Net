using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Control.Abstraction.UI;
using GalNet.Control.UI;
using GalNet.Core.Services;

namespace GalNet.Control.ViewModels;

public sealed partial class GameStartViewModel : ObservableObject
{
    private readonly IGameScreenNavigator _navigator;
    private readonly IGameExitService? _exitService;
    private readonly ISaveService? _saves;

    public string Title { get; }
    public WidgetHostViewModel ContinueHost { get; private set; } = null!;
    public WidgetHostViewModel NewGameHost { get; private set; } = null!;
    public WidgetHostViewModel LoadHost { get; private set; } = null!;
    public WidgetHostViewModel GalleryHost { get; private set; } = null!;
    public WidgetHostViewModel SettingsHost { get; private set; } = null!;
    public WidgetHostViewModel QuitHost { get; private set; } = null!;
    [ObservableProperty] private bool _isContinueVisible;
    [ObservableProperty] private bool _showGallery = true;

    public GameStartViewModel(IGameScreenNavigator navigator, IGameExitService? exitService, GameFlowOptions options, ISaveService? saves = null)
    {
        Title = options.Title ?? "GalNet Demo";
        _navigator = navigator; _exitService = exitService; _saves = saves;
        _ = RefreshContinueAsync();
    }

    public void SetHosts(WidgetHostViewModel continueHost, WidgetHostViewModel newGameHost, WidgetHostViewModel loadHost,
        WidgetHostViewModel galleryHost, WidgetHostViewModel settingsHost, WidgetHostViewModel quitHost, bool showGallery)
    {
        ContinueHost = continueHost; NewGameHost = newGameHost; LoadHost = loadHost;
        GalleryHost = galleryHost; SettingsHost = settingsHost; QuitHost = quitHost; ShowGallery = showGallery;
        Configure(ContinueHost, "Continue", ContinueCommand); Configure(NewGameHost, "New Game", NewGameCommand);
        Configure(LoadHost, "Load", LoadCommand); Configure(GalleryHost, "Gallery", GalleryCommand);
        Configure(SettingsHost, "Settings", SettingsCommand); Configure(QuitHost, "Quit", QuitCommand);
    }

    private static void Configure(WidgetHostViewModel host, string text, System.Windows.Input.ICommand command)
    { var button = host.RequireWidget<GalNet.Core.Widget.IButtonWidget>(); button.Text = text; button.Command = command; }

    [RelayCommand] private async Task ContinueAsync() { var snapshot = _saves is null ? null : await _saves.QuickLoadAsync(); if (snapshot is not null) await _navigator.NavigateAsync("game", snapshot); }
    [RelayCommand] private Task NewGameAsync() => _navigator.NavigateAsync("game");
    [RelayCommand] private Task LoadAsync() => _navigator.NavigateAsync("save-load");
    [RelayCommand] private Task GalleryAsync() => _navigator.NavigateAsync("gallery");
    [RelayCommand] private Task SettingsAsync() => _navigator.NavigateAsync("settings");
    [RelayCommand] private void Quit() { if (_exitService is not null) _exitService.Exit(); else Environment.Exit(0); }

    private async Task RefreshContinueAsync()
    {
        if (_saves is null || !await _saves.HasQuickSaveAsync().ConfigureAwait(false)) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => IsContinueVisible = true);
    }
}
