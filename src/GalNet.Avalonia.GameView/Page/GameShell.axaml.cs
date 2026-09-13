using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Diagnostics;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.ViewModels;

namespace GalNet.Avalonia.GameView.Page;

/// <summary>Reusable page host. Editors and players inject different services but reuse this layout.</summary>
public partial class GameShell : UserControl, IDisposable
{
    private static readonly TimeSpan PageTransitionDuration = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan TransitionTimeout = TimeSpan.FromSeconds(3);

    private readonly GameShellViewModel _viewModel;
    private readonly IPageViewFactory _views;
    private readonly IGameScreenshotService _screenshots;
    private readonly IGameSessionService _session;
    private readonly IGameNavigationService _navigation;
    private readonly GameNavigationTransitionCoordinator _transitions;
    private readonly LoadingPageViewModel _loading;
    private readonly SemaphoreSlim _transitionGate = new(1, 1);
    private GamePageViewModel? _gameplay;
    private bool _hadActiveRun;
    private bool _returningToTitle;
    private bool _disposed;

    public GameShell(
        GameShellViewModel viewModel,
        IPageViewFactory views,
        IGameScreenshotService screenshots,
        IGameSessionService session,
        IGameNavigationService navigation,
        GameNavigationTransitionCoordinator transitions,
        LoadingPageViewModel loading)
    {
        _viewModel = viewModel;
        _views = views;
        _screenshots = screenshots;
        _session = session;
        _navigation = navigation;
        _transitions = transitions;
        _loading = loading;
        InitializeComponent();
        DataContext = viewModel;
        session.PropertyChanged += OnSessionPropertyChanged;
        _transitions.Attach(ShowPageAsync, ShowLoadingAsync);
        ShowCurrentPage();
    }

    public void Dispose()
    {
        _disposed = true;
        _transitions.Detach();
        _session.PropertyChanged -= OnSessionPropertyChanged;
        if (_gameplay is not null)
        {
            _gameplay.ScreenshotRequested -= OnScreenshotRequested;
            _gameplay.ReturnToTitleRequested -= OnReturnToTitleRequested;
        }
    }

    private void OnSessionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName != nameof(IGameSessionService.IsPlaying)) return;
        if (_session.IsPlaying)
        {
            _hadActiveRun = true;
            return;
        }

        if (!_hadActiveRun || _returningToTitle) return;
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
        TrackGameplay(viewModel);
        PageHost.PageTransition = null;
        PageHost.Content = viewModel is not null ? _views.Create(viewModel) : null;
    }

    private async Task ShowPageAsync(PageViewModelBase? viewModel, NavigationTransition transition)
    {
        await _transitionGate.WaitAsync();
        try
        {
            await SetTransitionContentAsync(
                PageHost,
                () =>
                {
                    TrackGameplay(viewModel);
                    return viewModel is not null ? _views.Create(viewModel) : null;
                },
                transition == NavigationTransition.CrossFade);
        }
        finally { _transitionGate.Release(); }
    }

    private async Task ShowLoadingAsync(CancellationToken cancellationToken)
    {
        await _transitionGate.WaitAsync(cancellationToken);
        try
        {
            await SetTransitionContentAsync(
                PageHost,
                () =>
                {
                    _loading.Message = "Loading...";
                    return _views.Create(_loading);
                },
                animate: true,
                cancellationToken);
        }
        finally { _transitionGate.Release(); }
    }

    private async Task SetTransitionContentAsync(
        TransitioningContentControl host,
        Func<object?> contentFactory,
        bool animate,
        CancellationToken cancellationToken = default)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<TransitionCompletedEventArgs>? handler = null;
        var shouldWaitForTransition = false;
        object? targetContent = null;

        await OnUiAsync(() =>
        {
            if (_disposed)
            {
                completed.TrySetCanceled();
                return;
            }

            targetContent = contentFactory();
            if (targetContent is Visual targetVisual && targetVisual.GetVisualParent() is ContentPresenter previousPresenter)
            {
                // A scoped page view can still be held by an old presenter after the
                // previous transition. Detach only that stale presenter; keep the
                // current content so the next CrossFade still has an old page to fade out.
                previousPresenter.Content = null;
            }
            shouldWaitForTransition = animate &&
                                      (host.Content is not null || targetContent is not null) &&
                                      !ReferenceEquals(host.Content, targetContent);

            if (!shouldWaitForTransition)
            {
                host.PageTransition = null;
                host.Content = targetContent;
                completed.TrySetResult();
                return;
            }

            // Avalonia raises TransitionCompleted before it clears the old presenter.
            // Complete on the next UI turn so a scoped view can be reused safely.
            handler = (_, _) => Dispatcher.UIThread.Post(() => completed.TrySetResult());
            host.TransitionCompleted += handler;
            host.PageTransition = new CrossFade(PageTransitionDuration);
            try { host.Content = targetContent; }
            catch (Exception exception)
            {
                host.TransitionCompleted -= handler;
                completed.TrySetException(exception);
            }
        });

        try
        {
            if (shouldWaitForTransition)
                await completed.Task.WaitAsync(TransitionTimeout, cancellationToken);
            else
                await completed.Task.WaitAsync(cancellationToken);
        }
        catch (TimeoutException)
        {
            Trace.WriteLine($"GameShell transition timed out for {host.Name ?? host.GetType().Name}; forcing the target content.");
            await OnUiAsync(() =>
            {
                if (handler is not null) host.TransitionCompleted -= handler;
                host.PageTransition = null;
                host.Content = targetContent;
            });
        }
        finally
        {
            if (handler is not null)
                await OnUiAsync(() => host.TransitionCompleted -= handler);
        }
    }

    private static Task OnUiAsync(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(() =>
        {
            try { action(); completion.SetResult(); }
            catch (Exception exception) { completion.SetException(exception); }
        });
        return completion.Task;
    }

    private void TrackGameplay(PageViewModelBase? viewModel)
    {
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
    }

    private async void OnScreenshotRequested()
    {
        var page = PageHost.Content as GamePage;
        if (page is null) return;
        var owner = TopLevel.GetTopLevel(this);
        await _screenshots.CaptureAsync(new GameScreenshotRequest(
            _session.GameTitle,
            owner,
            includeUi => Task.FromResult(includeUi ? CapturePng(this) : page.CaptureScenePng())));
    }

    private async void OnReturnToTitleRequested()
    {
        if (_returningToTitle) return;
        _returningToTitle = true;
        try
        {
            await _session.StopAsync();
            if (_viewModel.CurrentViewModel is GamePageViewModel)
                _navigation.ResetTo<TitlePageViewModel>();
        }
        finally { _returningToTitle = false; }
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
