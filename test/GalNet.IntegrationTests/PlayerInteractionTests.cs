using PlayerView = GalNet.Sample.CrossPlatform.Views.PlayerView;
using GamePageViewModel = GalNet.Sample.CrossPlatform.ViewModels.GamePageViewModel;
using TitlePageViewModel = GalNet.Sample.CrossPlatform.ViewModels.TitlePageViewModel;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Interactivity;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Media;
using GalNet.Avalonia.Themes;
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
        host.Services.GetRequiredService<GalNet.Sample.CrossPlatform.Views.PlayerView>().CompleteStartup();
        var window = new Window { Content = host.View };
        window.Show();
        try
        {
            var game = host.Services.GetRequiredService<GalNet.Avalonia.GameView.Presentation.GamePresentationState>();
            game.ReturnToTitleCommand.Execute(null);
            var confirmation = host.Services.GetRequiredService<GalNet.Sample.CrossPlatform.Views.PlayerView>().ConfirmReturnToTitleAsync();
            Assert.That(host.View.FindControl<Grid>("PlayerContent")!.IsEnabled, Is.False);
            Assert.That(game.IsInteractionBlocked, Is.True);
            Assert.That(navigation.CurrentViewModel, Is.SameAs(host.Services.GetRequiredService<GamePageViewModel>()));
            Capture(window, "return-to-title");
            Assert.That(host.Services.GetRequiredService<GalNet.Sample.CrossPlatform.Views.PlayerView>().ConfirmReturnToTitleAsync(), Is.SameAs(confirmation));
            Assert.That(await host.ConfirmExitAsync(), Is.False);
            host.View.FindControl<Button>("CancelExitButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(await confirmation, Is.False);
            Assert.That(game.IsInteractionBlocked, Is.False);
            Assert.That(navigation.CurrentViewModel, Is.SameAs(host.Services.GetRequiredService<GamePageViewModel>()));
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
        var game = host.Services.GetRequiredService<GalNet.Avalonia.GameView.Presentation.GamePresentationState>();
        var controller = new PlaybackController();
        game.BindPlayback(controller);
        if (automatic) await game.ToggleAutoCommand.ExecuteAsync(null);
        else await game.SetQuickHeldAsync(true);
        var advanced = 0;
        game.AdvanceRequested += () => advanced++;
        var pending = host.Services.GetRequiredService<GalNet.Sample.CrossPlatform.Views.PlayerView>().ConfirmReturnToTitleAsync();
        Assert.That(controller.PlaybackMode, Is.EqualTo(PlaybackMode.Manual));
        game.AdvanceCommand.Execute(null);
        await game.ToggleQuickCommand.ExecuteAsync(null);
        Assert.That(advanced, Is.Zero);
        Assert.That(controller.PlaybackMode, Is.EqualTo(PlaybackMode.Manual));
        host.Services.GetRequiredService<GalNet.Sample.CrossPlatform.Views.PlayerView>().CancelExitConfirmation();
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
        var previousExtend = window.ExtendClientAreaToDecorationsHint;
        window.WindowState = WindowState.FullScreen;
        Assert.That(window.IsTitleBarVisible || window.CanResize || button.IsVisible, Is.False);
        Assert.That(window.WindowDecorations, Is.EqualTo(WindowDecorations.None));
        Assert.That(window.ExtendClientAreaToDecorationsHint, Is.False);
        window.WindowState = WindowState.Normal;
        Assert.That(window.IsTitleBarVisible && window.CanResize && button.IsVisible, Is.True);
        Assert.That(window.WindowDecorations, Is.EqualTo(previousDecorations));
        Assert.That(window.ExtendClientAreaToDecorationsHint, Is.EqualTo(previousExtend));
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

    [TestCase("Mono")]
    [TestCase("Ocean")]
    [TestCase("Rose")]
    public Task MusicToastExpiresAndDoesNotInterceptInput(string paletteName) => SampleUiTests.RunAsync(async () =>
    {
        await using var host = await AvaloniaPlayerHost.CreateAsync(SampleCustomization.CreateHostOptions(),
            services => SampleCustomization.ConfigureServices(services));
        var semi = new Semi.Avalonia.SemiTheme();
        var ursa = new Ursa.Themes.Semi.UrsaSemiTheme();
        Application.Current!.Styles.Add(semi); Application.Current.Styles.Add(ursa);
        host.View.Resources.MergedDictionaries.Add((paletteName switch { "Ocean" => GamePalette.Ocean, "Rose" => GamePalette.Rose, _ => GamePalette.Mono }).CreateResources());
        var window = new Window { Width = 800, Height = 450, Content = host.View };
        window.Show();
        try
        {
            var game = host.Services.GetRequiredService<GalNet.Avalonia.GameView.Presentation.GamePresentationState>();
            var manager = ((GalNet.Avalonia.GameHost.Views.PlayerSurface)host.View).TextCards;
            game.AnnounceAudio("Track one");
            await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Background);
            Assert.That(manager.IsHitTestVisible, Is.False);
            Assert.That(manager.Children, Is.Not.Empty);
            game.AnnounceAudio("Track two");
            await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Background);
            var card = manager.Children.OfType<ContentControl>().Single(card => Equals(card.Content, "Track two"));
            await Task.Delay(250);
            Assert.That(card.Bounds.Width, Is.GreaterThanOrEqualTo(280));
            var surface = card.GetVisualDescendants().OfType<Border>().First();
            Assert.That(((ISolidColorBrush)surface.Background!).Opacity, Is.EqualTo(.84));
            Assert.That(((ISolidColorBrush)card.Foreground!).Color, Is.EqualTo(((ISolidColorBrush)host.View.FindResource("GalNet.Brush.TextPrimary")!).Color));
            Assert.That(card.IsEffectivelyVisible, Is.True);
            Capture(window, $"music-toast-{paletteName}-entering");
            await Task.Delay(750);
            Capture(window, $"music-toast-{paletteName}-settled");
            Assert.That(card.IsEffectivelyVisible, Is.True);
            for (var attempt = 0; attempt < 100 && card.RenderTransform!.Value.M31 >= -1; attempt++) await Task.Delay(30);
            Assert.That(card.RenderTransform!.Value.M31, Is.LessThan(0));
            Capture(window, $"music-toast-{paletteName}-leaving");
            await Task.Delay(900);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Assert.That(manager.Children, Is.Empty);
        }
        finally { window.Close(); Application.Current.Styles.Remove(ursa); Application.Current.Styles.Remove(semi); }
    });
    [Test]
    public Task FullscreenBlocksNativeMoveAndResizeWithoutChangingNormalOrMaximizedBehavior() => SampleUiTests.RunAsync(() =>
    {
        var window = new GalNet.Sample.CrossPlatform.Views.MainWindow();
        var filter = window.GetType().GetMethod("FilterFullscreenMessages", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        foreach (var state in new[] { WindowState.Normal, WindowState.Maximized, WindowState.FullScreen })
        {
            window.WindowState = state;
            foreach (var (message, parameter) in new (uint, nint)[] { (0x84, 0), (0xA1, 2), (0xA3, 2), (0x112, 0xF012), (0x112, 0xF008) })
            {
                object[] arguments = [(nint)0, message, parameter, (nint)0, false];
                var result = (nint)filter.Invoke(window, arguments)!;
                Assert.That(arguments[4], Is.EqualTo(state == WindowState.FullScreen));
                if (state == WindowState.FullScreen && message == 0x84) Assert.That(result, Is.EqualTo((nint)1));
            }
            object[] close = [(nint)0, (uint)0x112, (nint)0xF060, (nint)0, false];
            filter.Invoke(window, close);
            Assert.That(close[4], Is.False, "Close must remain available in fullscreen.");
        }
        return Task.CompletedTask;
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
