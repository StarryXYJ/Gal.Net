using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Interactivity;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Avalonia.GameHost;
using GalNet.Avalonia.GameHost.Views;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Sample.CrossPlatform;
using GalNet.Presentation.Abstractions.Application;
using GalNet.Core.Execution;
using GalNet.Core.Primitives;
using GalNet.Presentation.Abstractions.View;
using Microsoft.Extensions.DependencyInjection;
using Ursa.Controls;

namespace GalNet.IntegrationTests;

[NonParallelizable]
public sealed class PlayerInteractionTests
{
    [Test]
    public Task ReturnToTitleRequiresConfirmationAndCannotAuthorizeWindowExit() => SampleUiTests.RunAsync(async () =>
    {
        await using var host = await AvaloniaPlayerHost.CreateAsync(SampleCustomization.CreateHostOptions(),
            services => SampleCustomization.ConfigureServices(services));
        var navigation = host.Services.GetRequiredService<IGameNavigationService>();
        navigation.ResetTo<GamePageViewModel>(NavigationTransition.None);
        host.View.CompleteStartup();
        var window = new Window { Content = host.View };
        window.Show();
        try
        {
            var game = host.Services.GetRequiredService<GamePageViewModel>();
            game.ReturnToTitleCommand.Execute(null);
            var confirmation = host.View.ConfirmReturnToTitleAsync();
            Assert.That(host.View.FindControl<Grid>("PlayerContent")!.IsEnabled, Is.False);
            Assert.That(game.IsInteractionBlocked, Is.True);
            Assert.That(navigation.CurrentViewModel, Is.SameAs(game));
            Capture(window, "return-to-title");
            Assert.That(host.View.ConfirmReturnToTitleAsync(), Is.SameAs(confirmation));
            Assert.That(await host.View.ConfirmExitAsync(), Is.False);
            host.View.FindControl<Button>("CancelExitButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(await confirmation, Is.False);
            Assert.That(game.IsInteractionBlocked, Is.False);
            Assert.That(navigation.CurrentViewModel, Is.SameAs(game));
            await Task.Yield();
            game.ReturnToTitleCommand.Execute(null);
            host.View.FindControl<Button>("ConfirmReturnToTitleButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var attempt = 0; attempt < 100 && navigation.CurrentViewModel is not TitlePageViewModel; attempt++) await Task.Delay(10);
            Assert.That(navigation.CurrentViewModel, Is.TypeOf<TitlePageViewModel>());
        }
        finally { window.Close(); }
    });

    [TestCase(true)]
    [TestCase(false)]
    public Task ModalBlocksAdvancingAndResumesOnlyAutomaticPlayback(bool automatic) => SampleUiTests.RunAsync(async () =>
    {
        await using var host = await AvaloniaPlayerHost.CreateAsync(SampleCustomization.CreateHostOptions(), services =>
        {
            SampleCustomization.ConfigureServices(services);
            services.AddSingleton<IGameSessionService>(new PlayingSession());
        });
        host.Services.GetRequiredService<IGameNavigationService>().ResetTo<GamePageViewModel>(NavigationTransition.None);
        var game = host.Services.GetRequiredService<GamePageViewModel>();
        var controller = new PlaybackController();
        game.BindPlayback(controller);
        if (automatic) await game.ToggleAutoCommand.ExecuteAsync(null);
        else await game.SetQuickHeldAsync(true);
        var advanced = 0;
        game.AdvanceRequested += () => advanced++;
        var pending = host.View.ConfirmReturnToTitleAsync();
        Assert.That(controller.PlaybackMode, Is.EqualTo(PlaybackMode.Manual));
        game.AdvanceCommand.Execute(null);
        await game.ToggleQuickCommand.ExecuteAsync(null);
        Assert.That(advanced, Is.Zero);
        Assert.That(controller.PlaybackMode, Is.EqualTo(PlaybackMode.Manual));
        host.View.CancelExitConfirmation();
        Assert.That(await pending, Is.False);
        Assert.That(controller.PlaybackMode, Is.EqualTo(automatic ? PlaybackMode.Auto : PlaybackMode.Manual));
    });

    [Test]
    public Task DetachCancelsPendingConfirmation() => SampleUiTests.RunAsync(async () =>
    {
        var view = new PlayerView();
        var pending = view.ConfirmReturnToTitleAsync();
        view.Detach();
        Assert.That(await pending, Is.False);
        Assert.That(view.FindControl<Grid>("PlayerContent")!.IsEnabled, Is.True);
    });

    [Test]
    public Task FitButtonAdjustsTheMeasuredContentBelowTheTitleBar() => SampleUiTests.RunAsync(async () =>
    {
        var semi = new Semi.Avalonia.SemiTheme();
        var ursa = new Ursa.Themes.Semi.UrsaSemiTheme();
        Application.Current!.Styles.Add(semi); Application.Current.Styles.Add(ursa);
        var content = new Border();
        var window = new GalNet.Sample.CrossPlatform.Views.MainWindow { Width = 1200, Height = 800, Content = content };
        window.Show();
        try
        {
            window.UpdateLayout();
            var button = window.FindControl<Button>("FitAspectButton")!;
            Assert.That(WindowDecorationProperties.GetElementRole(button), Is.EqualTo(global::Avalonia.Input.WindowDecorationsElementRole.User));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Background);
            Assert.That(content.Bounds.Width / content.Bounds.Height, Is.EqualTo(16d / 9).Within(.005));
            using var display = new AvaloniaWindowDisplayService();
            display.Bind(window);
            var size = window.ClientSize;
            var position = window.Position;
            await display.ApplyModeAsync(PlayerDisplayMode.BorderlessFullscreen, CancellationToken.None);
            await display.ApplyModeAsync(PlayerDisplayMode.Window, CancellationToken.None);
            Assert.That(window.Width, Is.EqualTo(size.Width).Within(1));
            Assert.That(window.Height, Is.EqualTo(size.Height).Within(1));
            Assert.That(window.Position, Is.EqualTo(position));
        }
        finally { window.Close(); Application.Current.Styles.Remove(ursa); Application.Current.Styles.Remove(semi); }
    });

    [Test]
    public Task FullscreenRemovesAllCaptionDragTargetsAndRestoresWindowControls() => SampleUiTests.RunAsync(() =>
    {
        var window = new GalNet.Sample.CrossPlatform.Views.MainWindow();
        var button = window.FindControl<Button>("FitAspectButton")!;
        var previousDecorations = window.WindowDecorations;
        window.WindowState = WindowState.FullScreen;
        Assert.That(window.IsTitleBarVisible || window.CanResize || button.IsVisible, Is.False);
        Assert.That(window.WindowDecorations, Is.EqualTo(WindowDecorations.None));
        window.WindowState = WindowState.Normal;
        Assert.That(window.IsTitleBarVisible && window.CanResize && button.IsVisible, Is.True);
        Assert.That(window.WindowDecorations, Is.EqualTo(previousDecorations));
        return Task.CompletedTask;
    });

    [TestCase(1200, 900, 32, 1920, 1080)]
    [TestCase(1400, 600, 40, 1200, 800)]
    [TestCase(600, 350, 32, 1920, 1080)]
    public void FitPreservesContentRatioAndRespectsChromeMinimumAndScreen(double width, double height, double chromeHeight, double screenWidth, double screenHeight)
    {
        var size = WindowAspectRatio.Fit(new(width, height), new(0, chromeHeight), new(800, 500), new(screenWidth, screenHeight), 16d / 9);
        Assert.That(size.Width / (size.Height - chromeHeight), Is.EqualTo(16d / 9).Within(.000001));
        Assert.That(size.Width, Is.InRange(800, screenWidth));
        Assert.That(size.Height, Is.InRange(500, screenHeight));
    }

    [Test]
    public Task MusicToastExpiresAndDoesNotInterceptInput() => SampleUiTests.RunAsync(async () =>
    {
        await using var host = await AvaloniaPlayerHost.CreateAsync(SampleCustomization.CreateHostOptions(),
            services => SampleCustomization.ConfigureServices(services));
        var semi = new Semi.Avalonia.SemiTheme();
        var ursa = new Ursa.Themes.Semi.UrsaSemiTheme();
        Application.Current!.Styles.Add(semi); Application.Current.Styles.Add(ursa);
        var window = new Window { Content = host.View };
        window.Show();
        try
        {
            var game = host.Services.GetRequiredService<GamePageViewModel>();
            var manager = host.View.FindControl<WindowToastManager>("AudioToast")!;
            game.AnnounceAudio("Track one");
            await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Background);
            Assert.That(manager.IsHitTestVisible, Is.False);
            Assert.That(manager.GetVisualDescendants().OfType<ToastCard>(), Is.Not.Empty);
            game.AnnounceAudio("Track two");
            await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Background);
            var cards = manager.GetVisualDescendants().OfType<ToastCard>().Where(card => !card.IsClosing).ToArray();
            Assert.That(cards, Has.Length.EqualTo(1));
            Assert.That(cards[0].Content, Is.EqualTo("Track two"));
            await Task.Delay(250);
            Assert.That(cards[0].Bounds.Width, Is.GreaterThan(0));
            Assert.That(cards[0].IsEffectivelyVisible, Is.True);
            Capture(window, "music-toast");
            await Task.Delay(2500);
            Assert.That(manager.GetVisualDescendants().OfType<ToastCard>().All(card => card.IsClosing), Is.True);
        }
        finally { window.Close(); Application.Current.Styles.Remove(ursa); Application.Current.Styles.Remove(semi); }
    });
    private sealed class PlayingSession : IGameSessionService
    {
        public string GameTitle => "Modal regression";
        public string StatusMessage => "";
        public bool IsReady => true;
        public bool IsPlaying => true;
        public bool CanContinue => false;
        public ReadOnlyObservableCollection<GameSaveSlot> SaveSlots { get; } = new(new ObservableCollection<GameSaveSlot>());
        public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
        public Task StartNewGameAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ContinueAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveAsync(int slotIndex, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(int slotIndex, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class PlaybackController : IPlayerPlaybackController
    {
        public PlaybackMode PlaybackMode { get; private set; }
        public event Action<PlaybackMode>? PlaybackModeChanged;
        public void CancelPlayback() => PlaybackMode = PlaybackMode.Manual;
        public Task SetPlaybackModeAsync(PlaybackMode mode, CancellationToken cancellationToken = default)
        { PlaybackMode = mode; PlaybackModeChanged?.Invoke(mode); return Task.CompletedTask; }
        public Task SkipToUnreadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdatePlaybackOptionsAsync(PlaybackOptions options, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static void Capture(Window window, string name)
    {
        var output = Environment.GetEnvironmentVariable("GALNET_THEME_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var bitmap = window.CaptureRenderedFrame();
        bitmap!.Save(Path.Combine(output, name + ".png"));
    }

}
