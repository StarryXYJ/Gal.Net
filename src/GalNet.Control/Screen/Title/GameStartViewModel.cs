using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Control.Abstraction.UI;
using GalNet.Core.Services;
using GalNet.Core.UI;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia;
using Avalonia.Media.Imaging;
using GalNet.Core.Assets;
using System.Globalization;

namespace GalNet.Control.ViewModels;

public partial class GameStartViewModel : ObservableObject
{
    private readonly IGameScreenNavigator _navigator; private readonly IGameExitService? _exit; private readonly ISaveService? _saves;
    public string Title { get; }
    public TitleUiConfiguration Configuration { get; }
    public Thickness TitleMenuMargin => new(0, 0, 0, Configuration.TitleMenuGap);
    public Thickness ContentMargin => new(Configuration.ContentPadding);
    public Stretch BackgroundStretch => Configuration.BackgroundStretch.ToLowerInvariant() switch { "uniform" => Stretch.Uniform, "fill" => Stretch.Fill, _ => Stretch.UniformToFill };
    public IBrush BackgroundBrush => new SolidColorBrush(Configuration.BackgroundColor);
    public IBrush TitleBrush => new SolidColorBrush(Configuration.TitleColor);
    public IBrush MenuTextBrush => new SolidColorBrush(Configuration.MenuTextColor);
    public IBrush MenuHoverTextBrush => new SolidColorBrush(Configuration.MenuHoverTextColor);
    public IBrush MenuItemBackgroundBrush => new SolidColorBrush(Configuration.ButtonColor);
    public IBrush MenuItemHoverBackgroundBrush => new SolidColorBrush(Configuration.ButtonHoverColor);
    public HorizontalAlignment MenuHorizontalAlignment { get; }
    public TextAlignment MenuTextAlignment { get; }
    [ObservableProperty] private bool _isContinueVisible;
    [ObservableProperty] private Bitmap? _backgroundImageSource;
    public bool HasBackgroundImage => BackgroundImageSource is not null;
    public bool ShowGallery => Configuration.ShowGallery;
    public GameStartViewModel(IGameScreenNavigator navigator, IGameExitService? exit, GameFlowOptions options, ISaveService? saves, TitleUiConfiguration config, string horizontalAlignment = "center", IAssetManager? assets = null)
    { _navigator = navigator; _exit = exit; _saves = saves; Configuration = config; Title = options.Title; MenuHorizontalAlignment = ParseHorizontal(horizontalAlignment); MenuTextAlignment = ParseTextAlignment(horizontalAlignment); _ = RefreshContinueAsync(); if (assets is not null) _ = LoadBackgroundAsync(assets); }
    [RelayCommand] private async Task ContinueAsync() { var snapshot = _saves is null ? null : await _saves.QuickLoadAsync(); if (snapshot is not null) await _navigator.NavigateAsync("game", snapshot); }
    [RelayCommand] private Task NewGameAsync() => _navigator.NavigateAsync("game");
    [RelayCommand] private Task LoadAsync() => _navigator.NavigateAsync("save-load");
    [RelayCommand] private Task GalleryAsync() => _navigator.NavigateAsync("gallery");
    [RelayCommand] private Task SettingsAsync() => _navigator.NavigateAsync("settings");
    [RelayCommand] private void Quit() { if (_exit is not null) _exit.Exit(); else Environment.Exit(0); }
    private async Task RefreshContinueAsync() { if (_saves is not null && await _saves.HasQuickSaveAsync()) Avalonia.Threading.Dispatcher.UIThread.Post(() => IsContinueVisible = true); }
    private async Task LoadBackgroundAsync(IAssetManager assets)
    {
        if (string.IsNullOrWhiteSpace(Configuration.BackgroundImage)) return;
        try
        {
            var file = await assets.GetFileAsync(Configuration.BackgroundImage);
            if (file?.Type != ResourceType.Sprite) return;
            // Read the file returned by the lookup directly. This keeps the selected
            // asset stable even when a provider refreshes between lookup and rendering.
            var bytes = await file.ReadAllBytesAsync();
            using var stream = new MemoryStream(bytes);
            var bitmap = new Bitmap(stream);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => BackgroundImageSource = bitmap);
        }
        catch { }
    }
    partial void OnBackgroundImageSourceChanged(Bitmap? value) => OnPropertyChanged(nameof(HasBackgroundImage));
    protected static HorizontalAlignment ParseHorizontal(string value) => value.ToLowerInvariant() switch { "left" => HorizontalAlignment.Left, "right" => HorizontalAlignment.Right, _ => HorizontalAlignment.Center };
    private static TextAlignment ParseTextAlignment(string value) => value.ToLowerInvariant() switch { "left" => TextAlignment.Left, "right" => TextAlignment.Right, _ => TextAlignment.Center };
}

/// <summary>Marker type selecting the text-menu page template while sharing title-page actions.</summary>
public sealed class TextMenuTitleViewModel : GameStartViewModel
{
    public TransformOperations HoverTransform { get; }

    public TextMenuTitleViewModel(IGameScreenNavigator navigator, IGameExitService? exit, GameFlowOptions options, ISaveService? saves, TitleUiConfiguration config, IReadOnlyDictionary<string, string> settings, IAssetManager? assets)
        : base(navigator, exit, options, saves, config, settings.GetValueOrDefault("horizontalAlignment", "center"), assets)
    {
        var scale = double.TryParse(settings.GetValueOrDefault("hoverScale"), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, 0.5, 2)
            : 1.08;
        HoverTransform = TransformOperations.Parse($"scale({scale.ToString(CultureInfo.InvariantCulture)})");
    }
}
