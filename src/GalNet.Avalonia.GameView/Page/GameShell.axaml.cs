using Avalonia.Controls;
using Avalonia;
using Avalonia.Media.Imaging;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.ViewModels;

namespace GalNet.Avalonia.GameView.Page;

/// <summary>Reusable page host. Editors and players inject different services but reuse this layout.</summary>
public partial class GameShell : UserControl, IDisposable
{
    private readonly GameShellViewModel _viewModel;
    private readonly IPageViewFactory _views;
    private readonly IGameScreenshotService _screenshots;
    private readonly IGameSessionService _session;
    private GamePageViewModel? _gameplay;

    public GameShell(
        GameShellViewModel viewModel,
        IPageViewFactory views,
        IGameScreenshotService screenshots,
        IGameSessionService session)
    {
        _viewModel = viewModel;
        _views = views;
        _screenshots = screenshots;
        _session = session;
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        ShowCurrentPage();
    }

    public void Dispose()
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        if (_gameplay is not null) _gameplay.ScreenshotRequested -= OnScreenshotRequested;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(GameShellViewModel.CurrentViewModel)) ShowCurrentPage();
    }

    private void ShowCurrentPage()
    {
        var viewModel = _viewModel.CurrentViewModel;
        if (!ReferenceEquals(_gameplay, viewModel))
        {
            if (_gameplay is not null) _gameplay.ScreenshotRequested -= OnScreenshotRequested;
            _gameplay = viewModel as GamePageViewModel;
            if (_gameplay is not null) _gameplay.ScreenshotRequested += OnScreenshotRequested;
        }
        PageHost.Content = viewModel is not null ? _views.Create(viewModel) : null;
    }

    private async void OnScreenshotRequested()
    {
        var page = PageHost.Content as GamePage;
        if (page is null) return;
        var owner = TopLevel.GetTopLevel(this);
        await _screenshots.CaptureAsync(new GameScreenshotRequest(
            _session.GameTitle,
            owner,
            includeUi => Task.FromResult(CapturePng(includeUi ? this : page.Scene))));
    }

    private static byte[] CapturePng(Control target)
    {
        var width = Math.Max(1, (int)Math.Ceiling(target.Bounds.Width));
        var height = Math.Max(1, (int)Math.Ceiling(target.Bounds.Height));
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height));
        bitmap.Render(target);
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        return stream.ToArray();
    }
}
