using Avalonia.Controls;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
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
    private readonly IGameNavigationService _navigation;
    private GamePageViewModel? _gameplay;
    private bool _hadActiveRun;

    public GameShell(
        GameShellViewModel viewModel,
        IPageViewFactory views,
        IGameScreenshotService screenshots,
        IGameSessionService session,
        IGameNavigationService navigation)
    {
        _viewModel = viewModel;
        _views = views;
        _screenshots = screenshots;
        _session = session;
        _navigation = navigation;
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        session.PropertyChanged += OnSessionPropertyChanged;
        ShowCurrentPage();
    }

    public void Dispose()
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _session.PropertyChanged -= OnSessionPropertyChanged;
        if (_gameplay is not null)
        {
            _gameplay.ScreenshotRequested -= OnScreenshotRequested;
            _gameplay.ReturnToTitleRequested -= OnReturnToTitleRequested;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(GameShellViewModel.CurrentViewModel)) ShowCurrentPage();
    }

    private void OnSessionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName != nameof(IGameSessionService.IsPlaying)) return;
        if (_session.IsPlaying)
        {
            _hadActiveRun = true;
            return;
        }

        if (!_hadActiveRun) return;
        _hadActiveRun = false;
        Dispatcher.UIThread.Post(() =>
        {
            // Do not rewrite history if a new run has already started or the host navigated
            // away while the old run was stopping.
            if (!_session.IsPlaying && _viewModel.CurrentViewModel is GamePageViewModel)
                _navigation.ResetTo<TitlePageViewModel>();
        });
    }

    private void ShowCurrentPage()
    {
        var viewModel = _viewModel.CurrentViewModel;
        if (!ReferenceEquals(_gameplay, viewModel))
        {
            if (_gameplay is not null)
            {
                _gameplay.ScreenshotRequested -= OnScreenshotRequested;
                _gameplay.ReturnToTitleRequested -= OnReturnToTitleRequested;
            }
            _gameplay = viewModel as GamePageViewModel;
            if (_gameplay is not null)
            {
                _gameplay.ScreenshotRequested += OnScreenshotRequested;
                _gameplay.ReturnToTitleRequested += OnReturnToTitleRequested;
            }
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

    private async void OnReturnToTitleRequested()
    {
        await _session.StopAsync();
        _navigation.ResetTo<TitlePageViewModel>();
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
