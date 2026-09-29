using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.ViewModels;
using Serilog;

namespace GalNet.Avalonia.GameView.Page;

/// <summary>Reusable page host. Editors and players inject different services but reuse this layout.</summary>
public partial class GameShell : UserControl, IDisposable
{
    private static readonly TimeSpan PageTransitionDuration = TimeSpan.FromMilliseconds(180);

    private readonly GameShellViewModel _viewModel;
    private readonly IPageViewFactory _views;
    private readonly IGameScreenshotService _screenshots;
    private readonly IGameSessionService _session;
    private readonly IGameNavigationService _navigation;
    private readonly GameNavigationTransitionCoordinator _transitions;
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
        GameNavigationTransitionCoordinator transitions)
    {
        _viewModel = viewModel;
        _views = views;
        _screenshots = screenshots;
        _session = session;
        _navigation = navigation;
        _transitions = transitions;
        InitializeComponent();
        DataContext = viewModel;
        session.PropertyChanged += OnSessionPropertyChanged;
        _transitions.Attach(ShowPageAsync);
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
                () =>
                {
                    TrackGameplay(viewModel);
                    return viewModel is not null ? _views.Create(viewModel) : null;
                },
                transition == NavigationTransition.CrossFade);
        }
        finally { _transitionGate.Release(); }
    }

    private async Task SetTransitionContentAsync(
        Func<object?> contentFactory,
        bool animate,
        CancellationToken cancellationToken = default)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<TransitionCompletedEventArgs>? handler = null;
        object? targetContent = null;
        var shouldWaitForTransition = false;

        await OnUiAsync(() =>
        {
            if (_disposed)
            {
                completed.TrySetCanceled(cancellationToken);
                return;
            }

            targetContent = contentFactory();
            if (targetContent is Visual targetVisual &&
                targetVisual.GetVisualParent() is ContentPresenter previousPresenter)
            {
                // TransitionCompleted is raised before Avalonia has necessarily removed the
                // outgoing presenter. A scoped page control can therefore still have a stale
                // parent when it is reused for the next run.
                previousPresenter.Content = null;
            }

            shouldWaitForTransition = animate &&
                                      (PageHost.Content is not null || targetContent is not null) &&
                                      !ReferenceEquals(PageHost.Content, targetContent);

            PageHost.ApplyTemplate();
            var presenterCount = PageHost.GetVisualDescendants().OfType<ContentPresenter>().Count();

            Log.Logger.Debug("Page transition prepared: from={From}, to={To}, animate={Animate}",
                PageHost.Content?.GetType().Name ?? "null",
                targetContent?.GetType().Name ?? "null",
                shouldWaitForTransition);
            Log.Logger.Debug("Page transition host state: attached={Attached}, template={Template}, presenters={Presenters}",
                PageHost.IsAttachedToVisualTree(), PageHost.Template is not null, presenterCount);

            if (!shouldWaitForTransition)
            {
                PageHost.PageTransition = null;
                PageHost.Content = targetContent;
                completed.TrySetResult();
                return;
            }

            handler = (_, _) =>
            {
                Log.Logger.Debug("Page crossfade completed: active={Active}",
                    targetContent?.GetType().Name ?? "null");
                // Avalonia raises TransitionCompleted before it removes the old presenter.
                // Complete on the next UI turn so a scoped view can be reused safely.
                Dispatcher.UIThread.Post(() => completed.TrySetResult());
            };
            PageHost.TransitionCompleted += handler;
            PageHost.PageTransition = new CrossFade(PageTransitionDuration);
            Log.Logger.Debug("Page crossfade started: from={From}, to={To}",
                PageHost.Content?.GetType().Name ?? "null",
                targetContent?.GetType().Name ?? "null");
            try { PageHost.Content = targetContent; }
            catch (Exception exception)
            {
                PageHost.TransitionCompleted -= handler;
                completed.TrySetException(exception);
            }
        });

        try
        {
            await completed.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            if (handler is not null)
            {
                await OnUiAsync(() =>
                {
                    PageHost.TransitionCompleted -= handler;
                });
            }
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
