using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using GalNet.Avalonia.GameHost.Settings;
using GalNet.Avalonia.GameView.Controls;
using GalNet.Avalonia.GameView.Input;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameHost;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Game.Controls;
using GalNet.Sample.CrossPlatform;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.IntegrationTests;

[NonParallelizable]
public sealed class ResponsiveLayoutTests
{
    [Test]
    public Task ReplayKeepsItsOwnStateAndDoesNotReserveHiddenToolbarSpace() => SampleUiTests.RunAsync(async () =>
    {
        await using var host = await ResponsiveHost();
        var replay = host.Services.GetRequiredService<GalleryScenePlayback>();
        replay.Gameplay.IsDialogueVisible = true;
        var page = replay.Surface;
        var root = new Border { Child = page, Classes = { "galnet-player", "compact-player" } };
        root.Styles.Add(SampleCustomization.CreateStyles());
        var window = new Window { Width = 800, Height = 360, Content = root };
        window.Show();
        try
        {
            window.UpdateLayout();
            Assert.That(page.Presentation, Is.Not.SameAs(host.Services.GetRequiredService<GamePresentationState>()));
            Assert.That(page.GetVisualDescendants().OfType<Button>().Any(button => button.Classes.Contains("player-link") && button.IsEffectivelyVisible), Is.False);
            var bottom = page.Dialogue.TranslatePoint(new Point(0, page.Dialogue.Bounds.Height), page)!.Value.Y;
            Assert.That(bottom, Is.EqualTo(360 - 70d / 3).Within(1));
        }
        finally { window.Close(); }
    });

    [Test]
    public Task ResponsiveShellCanDetachAndReattachWithoutDuplicateParentsOrSubscriptions() => SampleUiTests.RunAsync(async () =>
    {
        await using var host = await ResponsiveHost();
        var application = host.Services.GetRequiredService<IPlayerApplication>();
        var shell = host.Services.GetRequiredService<GameShell>();
        var content = host.View.FindControl<Grid>("PlayerContent")!;
        application.Detach();
        Assert.That(content.Children, Does.Not.Contain(shell));
        Assert.That(host.View.FindControl<GameCanvasHost>("GameCanvas")!.GameContent, Is.Null);
        application.Attach();
        Assert.That(content.Children.Count(child => ReferenceEquals(child, shell)), Is.EqualTo(1));
        application.CompleteStartup("Startup failure");
        Assert.That(shell.IsVisible, Is.False);
        application.CompleteStartup(null);
        Assert.That(shell.IsVisible, Is.True);
    });

    [TestCase(1600, 900, false)]
    [TestCase(1200, 240, true)]
    public Task WideToolbarKeepsAllActionsAndUsesBlackBordersOnlyWhenNeeded(int width, int height, bool expands) => SampleUiTests.RunAsync(async () =>
    {
        await using var host = await ResponsiveHost();
        host.Services.GetRequiredService<IGameNavigationService>().ResetTo<GalNet.Sample.CrossPlatform.ViewModels.GamePageViewModel>(NavigationTransition.None);
        host.Services.GetRequiredService<GamePresentationState>().CompleteOpeningPresentation();
        var page = host.Services.GetRequiredService<GamePage>();
        var window = new Window { Width = width, Height = height, Content = host.View };
        window.Show();
        try
        {
            window.UpdateLayout();
            var frame = page.GetVisualDescendants().OfType<Border>().Single(item => item.Name == "ToolbarFrame");
            var visible = page.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("player-link") && button.IsEffectivelyVisible).ToArray();
            Assert.That(visible.Length, Is.EqualTo(9));
            var sceneWidth = Math.Min(width, height * 1920d / 1080);
            Assert.That(frame.Bounds.Width > sceneWidth + .1, Is.EqualTo(expands));
            Assert.That(frame.Bounds.Width, Is.LessThanOrEqualTo(width - 16));
            Capture(window, $"wide-{width}x{height}");
        }
        finally { window.Close(); }
    });

    [Test]
    public Task InputBindingRowsWrapAndKeepAnActiveCaptureAcrossResize() => SampleUiTests.RunAsync(() =>
    {
        using var settings = new PlayerInputSettings();
        using var panel = new InputBindingsPanel(settings);
        var root = new Border { Child = panel, Width = 340, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left, Classes = { "galnet-player", "compact-player" } };
        root.Styles.Add(SampleCustomization.CreateStyles());
        var window = new Window { Width = 900, Height = 600, Content = root };
        window.Styles.Add(new global::Avalonia.Themes.Fluent.FluentTheme());
        window.Show();
        try
        {
            window.UpdateLayout();
            var row = panel.GetVisualDescendants().OfType<Grid>().First(grid => grid.Children.Count == 4 && grid.Children[1] is InputBindingCapture);
            var capture = (InputBindingCapture)row.Children[1];
            Assert.That(Grid.GetRow(capture), Is.EqualTo(1));
            Assert.That(Grid.GetRow(row.Children[2]), Is.EqualTo(2));
            capture.BeginCapture();
            Assert.That(settings.IsCapturing, Is.True);
            root.Width = 850;
            window.UpdateLayout();
            Assert.That(row.Children[1], Is.SameAs(capture));
            Assert.That(Grid.GetRow(capture), Is.Zero);
            Assert.That(settings.IsCapturing, Is.True);
            root.Width = 340;
            window.UpdateLayout();
            foreach (var item in panel.GetVisualDescendants().OfType<TabItem>())
                Assert.That(item.TranslatePoint(default, panel)!.Value.X + item.Bounds.Width, Is.LessThanOrEqualTo(340));
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [TestCase(360, 800)]
    [TestCase(800, 360)]
    public Task OrdinaryPagesAndConfirmationStayInsideTheViewport(int width, int height) => SampleUiTests.RunAsync(async () =>
    {
        await using var host = await ResponsiveHost();
        var navigation = host.Services.GetRequiredService<IGameNavigationService>();
        var window = new Window { Width = width, Height = height, Content = host.View };
        window.Show();
        try
        {
            window.UpdateLayout();
            var titleButton = window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "NewGameButton");
            Assert.That(titleButton.Bounds.Height, Is.GreaterThanOrEqualTo(40));
            Capture(window, $"title-{width}x{height}");
            navigation.ResetTo<GalNet.Sample.CrossPlatform.ViewModels.SaveSlotsPageViewModel>(NavigationTransition.None);
            var model = host.Services.GetRequiredService<GalNet.Sample.CrossPlatform.ViewModels.SaveSlotsPageViewModel>();
            for (var index = 0; index < 8; index++) model.Slots.Add(new(new(index, default, "", true, false), false, _ => Task.CompletedTask, _ => Task.CompletedTask));
            window.UpdateLayout();
            var grid = window.GetVisualDescendants().OfType<GalNet.Sample.CrossPlatform.Views.AdaptiveSaveGrid>().Single();
            Assert.That(grid.Columns, Is.EqualTo(width < 600 ? 1 : 3));
            Capture(window, $"slots-{width}x{height}");
            navigation.ResetTo<GalNet.Sample.CrossPlatform.ViewModels.GalleryPageViewModel>(NavigationTransition.None);
            window.UpdateLayout();
            var frame = window.GetVisualDescendants().OfType<Border>().Single(item => item.Name == "PageFrame");
            Assert.That(frame.Bounds.Width, Is.LessThanOrEqualTo(width));
            Assert.That(window.GetVisualDescendants().OfType<Button>().Single(item => item.Name == "PageBackButton").Bounds.Height, Is.GreaterThanOrEqualTo(40));
            var confirmation = host.ConfirmExitAsync();
            window.UpdateLayout();
            var cancel = host.View.FindControl<Button>("CancelExitButton")!;
            Assert.That(cancel.Bounds.Height, Is.InRange(36, 40));
            Assert.That(cancel.TranslatePoint(default, host.View)!.Value.X + cancel.Bounds.Width, Is.LessThanOrEqualTo(width));
            cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(await confirmation, Is.False);
        }
        finally { window.Close(); }
    });

    [TestCase(800, 360, FontSizePreset.Smaller)]
    [TestCase(1280, 720, FontSizePreset.Smaller)]
    [TestCase(800, 360, FontSizePreset.Larger)]
    [TestCase(960, 432, FontSizePreset.Larger)]
    [TestCase(360, 800, FontSizePreset.Larger)]
    [TestCase(360, 800, FontSizePreset.Large)]
    [TestCase(320, 800, FontSizePreset.Large)]
    public Task GameKeepsFixedCanvasAndTouchableCenteredToolbar(int width, int height, FontSizePreset preset) => SampleUiTests.RunAsync(async () =>
    {
        await using var host = await ResponsiveHost(preset);
        var navigation = host.Services.GetRequiredService<IGameNavigationService>();
        navigation.ResetTo<GalNet.Sample.CrossPlatform.ViewModels.GamePageViewModel>(NavigationTransition.None);
        var state = host.Services.GetRequiredService<GamePresentationState>();
        state.IsDialogueVisible = true;
        state.CompleteOpeningPresentation();
        state.GetHistoryLines = () => [];
        var page = host.Services.GetRequiredService<GamePage>();
        page.Dialogue.CharactersPerSecond = 0;
        page.Dialogue.Speaker = "角色名 / Speaker";
        page.Dialogue.Text = "场景与对白保留固定画布，按钮独立适配。The scene keeps its composition.";
        var window = new Window { Width = width, Height = height, Content = host.View };
        window.Styles.Add(new global::Avalonia.Themes.Fluent.FluentTheme());
        window.Show();
        try
        {
            window.UpdateLayout();
            await page.Dialogue.StartAsync();
            window.UpdateLayout();
            Assert.That(host.Services.GetRequiredService<GameShell>().Bounds.Size, Is.EqualTo(new Size(width, height)));
            Assert.That(host.View.FindControl<GameCanvasHost>("GameCanvas")!.GameContent, Is.Null);
            Assert.That(page.Scene.Bounds.Size, Is.EqualTo(new Size(1920, 1080)));
            var frame = page.GetVisualDescendants().OfType<Border>().Single(item => item.Name == "ToolbarFrame");
            var buttons = page.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("player-link") && button.IsEffectivelyVisible).ToArray();
            Assert.That(buttons.Length, Is.AnyOf(4, 10));
            foreach (var button in buttons)
            {
                var visual = button.GetVisualDescendants().OfType<global::Avalonia.Controls.Presenters.ContentPresenter>().Single(item => item.Name == "ToolbarContentPresenter");
                Assert.That(button.FontSize, Is.LessThanOrEqualTo(18));
                Assert.That(visual.Bounds.Height, Is.LessThanOrEqualTo(30));
                Assert.That(button.Bounds.Height - visual.Bounds.Height, Is.GreaterThanOrEqualTo(4));
                Assert.That(((global::Avalonia.Media.ISolidColorBrush)visual.Background!).Color.A, Is.Zero);
            }
            Assert.That(buttons.All(button => button.Bounds.Width >= 48 && button.Bounds.Height >= 32 && button.Bounds.Height <= 36), Is.True);
            foreach (var button in buttons)
            {
                var left = button.TranslatePoint(default, page)!.Value.X;
                Assert.That(left, Is.GreaterThanOrEqualTo(7));
                Assert.That(left + button.Bounds.Width, Is.LessThanOrEqualTo(width - 7));
            }
            Assert.That(frame.Bounds.Center.X, Is.EqualTo(width / 2d).Within(1));
            Assert.That(((global::Avalonia.Media.ISolidColorBrush)frame.Background!).Color.A, Is.Zero, "Touch targets must not become a large visible toolbar plate.");
            Assert.That(frame.Bounds.Width, Is.LessThanOrEqualTo(width - 16));
            var dialogueBottom = page.Dialogue.TranslatePoint(new Point(0, page.Dialogue.Bounds.Height), page)!.Value.Y;
            Assert.That(frame.Bounds.Top - dialogueBottom, Is.EqualTo(2).Within(1));
            var sceneBottom = (height + 1080 * Math.Min(width / 1920d, height / 1080d)) / 2;
            Assert.That(frame.Bounds.Bottom, Is.EqualTo(Math.Min(height, sceneBottom)).Within(1));
            using (var scene = page.SceneHost.CaptureControl(page.Scene)) Assert.That(scene.PixelSize, Is.EqualTo(new PixelSize(1920, 1080)));
            using (var screen = page.SceneHost.CaptureControl(host.Services.GetRequiredService<GameShell>())) Assert.That(screen.PixelSize, Is.EqualTo(new PixelSize(width, height)));
            Capture(window, $"game-{width}x{height}");
            var more = buttons.SingleOrDefault(button => button.Name == "MoreButton");
            if (more is null) return;
            more.Flyout!.ShowAt(more);
            window.UpdateLayout();
            var menu = (MenuFlyout)more.Flyout;
            Assert.That(menu.IsOpen, Is.True);
            Capture(window, $"more-{width}x{height}");
            var settings = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header?.ToString(), "Settings"));
            Assert.That(settings.FontSize, Is.InRange(14, 18));
            Assert.That(settings.Command, Is.SameAs(host.Services.GetRequiredService<GalNet.Sample.CrossPlatform.ViewModels.GamePageViewModel>().OpenSettingsCommand));
            Assert.That(await host.Services.GetRequiredService<IPlayerApplication>().HandleBackAsync(), Is.False);
            Assert.That(menu.IsOpen, Is.False);
            Assert.That(navigation.CurrentViewModel, Is.TypeOf<GalNet.Sample.CrossPlatform.ViewModels.GamePageViewModel>());
            more.Flyout.ShowAt(more);
            navigation.ResetTo<GalNet.Sample.CrossPlatform.ViewModels.SettingsPageViewModel>(NavigationTransition.None);
            window.UpdateLayout();
            Assert.That(menu.IsOpen, Is.False, "Leaving the page closes its popup.");
        }
        finally { window.Close(); }
    });

    [Test]
    public Task NarrowSettingsReflowWithoutRecreatingEditorsOrLosingValues() => SampleUiTests.RunAsync(async () =>
    {
        await using var host = await ResponsiveHost();
        host.Services.GetRequiredService<IGameNavigationService>().ResetTo<GalNet.Sample.CrossPlatform.ViewModels.SettingsPageViewModel>(NavigationTransition.None);
        var window = new Window { Width = 800, Height = 400, Content = host.View };
        window.Show();
        try
        {
            window.UpdateLayout();
            var page = window.GetVisualDescendants().OfType<GalNet.Sample.CrossPlatform.Views.SettingsPage>().Single();
            var back = page.FindControl<Button>("PageBackButton")!;
            Assert.That(back.Bounds.Height, Is.GreaterThanOrEqualTo(40));
            var row = page.GetVisualDescendants().OfType<SettingRow>().First(item => item.Children[0].IsVisible);
            var editor = row.Children[1];
            Assert.That(Grid.GetRow(editor), Is.Zero);
            var value = host.Services.GetRequiredService<AvaloniaPlayerSettingsService>().RuntimeSettings.TextSpeed;
            host.View.Width = 360;
            host.View.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left;
            window.UpdateLayout();
            Assert.That(row.Children[1], Is.SameAs(editor));
            Assert.That(Grid.GetRow(editor), Is.EqualTo(1));
            Assert.That(host.Services.GetRequiredService<AvaloniaPlayerSettingsService>().RuntimeSettings.TextSpeed, Is.EqualTo(value));
            Assert.That(page.FindControl<Border>("PageFrame")!.Bounds.Width, Is.LessThanOrEqualTo(360));
            Capture(window, "settings-360");
            host.View.Width = 760;
            window.UpdateLayout();
            Assert.That(row.Children[1], Is.SameAs(editor));
            Assert.That(Grid.GetRow(editor), Is.Zero);
        }
        finally { window.Close(); }
    });

    [TestCase(320, 1)]
    [TestCase(760, 3)]
    [TestCase(1100, 4)]
    public Task TilesUseAvailableWidth(int width, int columns) => SampleUiTests.RunAsync(() =>
    {
        var slots = new GalNet.Sample.CrossPlatform.Views.AdaptiveSaveGrid();
        var gallery = new GalNet.Sample.CrossPlatform.Views.AdaptiveGalleryPanel();
        for (var index = 0; index < 8; index++)
        { slots.Children.Add(new Border { Height = 180 }); gallery.Children.Add(new Border()); }
        slots.Measure(new Size(width, double.PositiveInfinity));
        gallery.Measure(new Size(width, double.PositiveInfinity));
        Assert.That(slots.Columns, Is.EqualTo(columns));
        Assert.That(gallery.ItemWidth, Is.LessThanOrEqualTo(width));
        Assert.That(gallery.ItemHeight / gallery.ItemWidth, Is.EqualTo(190d / 260).Within(.001));
        return Task.CompletedTask;
    });

    [Test]
    public Task TouchOnToolbarSpaceAndSlidersDoesNotDispatchStoryGestures() => SampleUiTests.RunAsync(async () =>
    {
        using var settings = new PlayerInputSettings();
        settings.CommitAsync = value => { settings.Apply(value); return Task.FromResult(true); };
        Assert.That(await settings.SetBindingAsync("toggle.auto", PlayerInputDevice.Touch, 0, "DoubleTap"), Is.True);
        var calls = 0;
        var toolbar = new Border { Width = 100, Height = 100 };
        GamePage.SetIsPlayerInteraction(toolbar, true);
        var slider = new Slider { Width = 100 };
        var root = new StackPanel { Children = { toolbar, slider } };
        var window = new Window { Width = 300, Height = 300, Content = root };
        using var touch = new TouchInputRecognizer(root, settings, () => true, _ => { calls++; return Task.CompletedTask; });
        window.Show();
        try
        {
            foreach (var target in new Control[] { root, toolbar, slider })
            {
                var pointer = new Pointer(1, PointerType.Touch, true);
                target.RaiseEvent(new PointerPressedEventArgs(target, pointer, target, new Point(10, 10), 0,
                    new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));
                target.RaiseEvent(new PointerReleasedEventArgs(target, pointer, target, new Point(10, 10), 0,
                    new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
            }
            await Task.Delay(320);
            Assert.That(calls, Is.Zero);
        }
        finally { window.Close(); }
    });

    [Test]
    public Task DesktopResizeReflowsTheSameShellAndRestoresWideTitleLayout() => SampleUiTests.RunAsync(async () =>
    {
        await using var host = await AvaloniaPlayerHost.CreateAsync(SampleCustomization.CreateHostOptions(), services => SampleCustomization.ConfigureServices(services));
        host.Services.GetRequiredService<IPlayerApplication>().CompleteStartup(null);
        var shell = host.Services.GetRequiredService<GameShell>();
        var window = new Window { Width = 1280, Height = 720, Content = host.View };
        window.Show();
        try
        {
            window.UpdateLayout();
            var title = window.GetVisualDescendants().OfType<GalNet.Sample.CrossPlatform.Views.TitlePage>().Single();
            var layout = title.FindControl<Grid>("TitleLayout")!;
            var button = title.FindControl<Button>("NewGameButton")!;
            Assert.That(layout.ColumnDefinitions[0].Width.Value, Is.EqualTo(320));
            Assert.That(shell.Classes.Contains("compact-player"), Is.False);
            foreach (var width in new[] { 360, 1280, 800, 1280 })
            {
                host.View.Width = width;
                host.View.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left;
                window.UpdateLayout();
                Assert.That(host.Services.GetRequiredService<GameShell>(), Is.SameAs(shell));
                Assert.That(shell.Bounds.Width, Is.EqualTo(width));
                Assert.That(shell.Classes.Contains("compact-player"), Is.EqualTo(width < 1000));
                Assert.That(layout.ColumnDefinitions[1].Width.Value, width < 1000 ? Is.Zero : Is.EqualTo(1));
                Assert.That(title.FindControl<Button>("NewGameButton"), Is.SameAs(button));
                Assert.That(button.TranslatePoint(default, host.View)!.Value.X + button.Bounds.Width, Is.LessThanOrEqualTo(width));
                Assert.That(shell.Resources.ContainsKey("GalNet.Theme.PageHeaderMinHeight"), Is.EqualTo(width < 1000));
                Capture(window, $"desktop-title-{width}");
            }
            host.Services.GetRequiredService<IPlayerApplication>().Detach();
            Assert.That(shell.Classes.Contains("compact-player"), Is.False);
            Assert.That(shell.Resources.ContainsKey("GalNet.Theme.PageHeaderMinHeight"), Is.False);
        }
        finally { window.Close(); }
    });

    [Test]
    public Task DesktopToolbarClosesMoreWhenResizeRestoresFullActions() => SampleUiTests.RunAsync(async () =>
    {
        await using var host = await ResponsiveHost(FontSizePreset.Smaller);
        host.Services.GetRequiredService<IGameNavigationService>().ResetTo<GalNet.Sample.CrossPlatform.ViewModels.GamePageViewModel>(NavigationTransition.None);
        var state = host.Services.GetRequiredService<GamePresentationState>();
        state.CompleteOpeningPresentation();
        var page = host.Services.GetRequiredService<GamePage>();
        var window = new Window { Width = 1280, Height = 720, Content = host.View };
        window.Styles.Add(new global::Avalonia.Themes.Fluent.FluentTheme());
        host.View.Width = 360;
        host.View.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left;
        window.Show();
        try
        {
            window.UpdateLayout();
            var more = page.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "MoreButton");
            Assert.That(more.IsEffectivelyVisible, Is.True);
            more.Flyout!.ShowAt(more);
            window.UpdateLayout();
            Assert.That(((MenuFlyout)more.Flyout).IsOpen, Is.True);
            host.View.Width = 1280;
            window.UpdateLayout();
            Assert.That(((MenuFlyout)more.Flyout).IsOpen, Is.False);
            Assert.That(more.IsEffectivelyVisible, Is.False);
            Assert.That(host.Services.GetRequiredService<GamePage>(), Is.SameAs(page));
            Assert.That(page.GetVisualDescendants().OfType<Button>().Count(button => button.Classes.Contains("player-link") && button.IsEffectivelyVisible), Is.EqualTo(9));
            Capture(window, "desktop-toolbar-expanded");
        }
        finally { window.Close(); }
    });

    [TestCase(360, 800, FontSizePreset.Larger)]
    [TestCase(800, 360, FontSizePreset.Larger)]
    [TestCase(1280, 720, FontSizePreset.Smaller)]
    [TestCase(1920, 1080, FontSizePreset.Smaller)]
    [TestCase(2560, 1440, FontSizePreset.Smaller)]
    public Task ViewportDensityPreservesSceneFontsAndGivesButtonsVisibleFeedback(int width, int height, FontSizePreset preset) => SampleUiTests.RunAsync(async () =>
    {
        await using var host = await ResponsiveHost(preset);
        var window = new Window { Width = width, Height = height, Content = host.View };
        window.Styles.Add(new global::Avalonia.Themes.Fluent.FluentTheme());
        window.Show();
        try
        {
            window.UpdateLayout();
            var sizes = PlayerTypography.Presets[preset];
            var shell = host.Services.GetRequiredService<GameShell>();
            var scale = PlayerLayout.UiScale(new Size(width, height));
            Assert.That(shell.Resources["GalNet.Theme.TitleSize"], Is.EqualTo(sizes.Title * scale));
            Assert.That(shell.Resources["GalNet.Theme.BodyFontSize"], Is.EqualTo(sizes.Body * scale));
            Assert.That(shell.Resources["GalNet.Theme.DialogueFontSize"], Is.EqualTo(sizes.Dialogue));
            Assert.That(shell.Resources["GalNet.Theme.SpeakerFontSize"], Is.EqualTo(sizes.Emphasis));
            Capture(window, $"audit-title-{width}x{height}");
            var navigation = host.Services.GetRequiredService<IGameNavigationService>();
            foreach (var type in new[] { typeof(GalNet.Sample.CrossPlatform.ViewModels.SettingsPageViewModel), typeof(GalNet.Sample.CrossPlatform.ViewModels.SaveSlotsPageViewModel), typeof(GalNet.Sample.CrossPlatform.ViewModels.GalleryPageViewModel), typeof(GalNet.Sample.CrossPlatform.ViewModels.AboutPageViewModel) })
            {
                await navigation.NavigateAsync((GalNet.Avalonia.GameView.Navigation.PageViewModelBase)host.Services.GetRequiredService(type), NavigationTransition.None);
                window.UpdateLayout();
                var back = window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "PageBackButton");
                var point = back.TranslatePoint(default, host.View)!.Value;
                Assert.That(point.X, Is.GreaterThanOrEqualTo(0));
                Assert.That(point.X + back.Bounds.Width, Is.LessThanOrEqualTo(width));
                Assert.That(back.Bounds.Height, Is.InRange(36, 44));
                if (type == typeof(GalNet.Sample.CrossPlatform.ViewModels.SettingsPageViewModel) && width >= 1000)
                {
                    var languageRow = window.GetVisualDescendants().OfType<SettingRow>().First(row => row.IsEffectivelyVisible);
                    Assert.That(languageRow.Bounds.Height, Is.LessThanOrEqualTo(44), "A field without an error must not reserve an empty validation line.");
                }
                Capture(window, $"audit-{type.Name}-{width}x{height}");
            }
            navigation.ResetTo<GalNet.Sample.CrossPlatform.ViewModels.GamePageViewModel>(NavigationTransition.None);
            var state = host.Services.GetRequiredService<GamePresentationState>();
            state.CompleteOpeningPresentation();
            state.IsDialogueVisible = true;
            var page = host.Services.GetRequiredService<GamePage>();
            page.Dialogue.Speaker = "角色名 / Speaker";
            page.Dialogue.CharactersPerSecond = 0;
            page.Dialogue.Text = "保持场景构图，外部界面按窗口调整。The scene keeps its composition.";
            window.UpdateLayout();
            await page.Dialogue.StartAsync();
            window.UpdateLayout();
            Assert.That(page.Choice.FontSize, Is.EqualTo(sizes.Body));
            var quick = page.GetVisualDescendants().OfType<ToggleButton>().Single(button => button.IsEffectivelyVisible && button.Command == state.ToggleQuickCommand);
            var normal = ((global::Avalonia.Media.ISolidColorBrush)quick.Foreground!).Color;
            Capture(window, $"audit-game-{width}x{height}");
            var center = quick.TranslatePoint(new Point(quick.Bounds.Width / 2, quick.Bounds.Height / 2), window)!.Value;
            window.MouseMove(center);
            Assert.That(((global::Avalonia.Media.ISolidColorBrush)quick.Foreground!).Color, Is.Not.EqualTo(normal));
            Assert.That(((global::Avalonia.Media.ISolidColorBrush)quick.BorderBrush!).Color.A, Is.GreaterThan(0));
            Assert.That(((global::Avalonia.Media.ISolidColorBrush)quick.Background!).Color.A, Is.Zero);
            Capture(window, $"audit-hover-{width}x{height}");
            state.IsSkipConfirmationOpen = true; // Visual fixture has no runtime playback controller.
            window.UpdateLayout();
            var skipConfirm = window.GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.Command, state.ConfirmSkipCommand));
            var skipPoint = skipConfirm.TranslatePoint(default, host.View)!.Value;
            Assert.That(skipPoint.X, Is.GreaterThanOrEqualTo(0));
            Assert.That(skipPoint.X + skipConfirm.Bounds.Width, Is.LessThanOrEqualTo(width));
            Assert.That(skipConfirm.Classes.Contains("player-primary"), Is.True);
            Capture(window, $"audit-skip-{width}x{height}");
            state.CancelSkipCommand.Execute(null);
            var confirm = host.ConfirmExitAsync();
            window.UpdateLayout();
            var cancel = host.View.FindControl<Button>("CancelExitButton")!;
            var primary = host.View.FindControl<Button>("ConfirmExitButton")!;
            Assert.That(cancel.Bounds.Height, Is.InRange(36, 40));
            Assert.That(primary.Bounds.Height, Is.EqualTo(cancel.Bounds.Height));
            Assert.That(((global::Avalonia.Media.ISolidColorBrush)primary.Background!).Color, Is.Not.EqualTo(((global::Avalonia.Media.ISolidColorBrush)cancel.Background!).Color));
            Capture(window, $"audit-confirm-{width}x{height}");
            cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(await confirm, Is.False);
            var returnConfirm = ((GalNet.Sample.CrossPlatform.Views.PlayerView)host.View).ConfirmReturnToTitleAsync();
            window.UpdateLayout();
            var returnButton = host.View.FindControl<Button>("ConfirmReturnToTitleButton")!;
            Assert.That(returnButton.IsEffectivelyVisible, Is.True);
            Assert.That(returnButton.Bounds.Height, Is.EqualTo(cancel.Bounds.Height));
            Capture(window, $"audit-return-{width}x{height}");
            cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(await returnConfirm, Is.False);
        }
        finally { window.Close(); }
    });

    private static async Task<AvaloniaPlayerHost> ResponsiveHost(FontSizePreset preset = FontSizePreset.Larger)
    {
        var host = await AvaloniaPlayerHost.CreateAsync(SampleCustomization.CreateHostOptions(), services =>
        {
            SampleCustomization.ConfigureServices(services);
            services.AddScoped(_ =>
            {
                var view = SampleCustomization.CreateView();
                return view;
            });
            services.AddScoped(provider => new AvaloniaPlayerSettingsService(provider.GetRequiredService<IPlayerLocalization>(),
                provider.GetRequiredService<GamePresentationState>(), defaultFontSize: preset));
        });
        host.Services.GetRequiredService<IPlayerApplication>().CompleteStartup(null);
        return host;
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("GALNET_MOBILE_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var image = window.CaptureRenderedFrame();
        image!.Save(Path.Combine(directory, name + ".png"));
    }

    [Test]
    public Task DesktopGameUsesActualViewportAndKeepsSceneComposition() => SampleUiTests.RunAsync(async () =>
    {
        await using var host = await AvaloniaPlayerHost.CreateAsync(SampleCustomization.CreateHostOptions(), services => SampleCustomization.ConfigureServices(services));
        host.Services.GetRequiredService<IPlayerApplication>().CompleteStartup(null);
        host.Services.GetRequiredService<IGameNavigationService>().ResetTo<GalNet.Sample.CrossPlatform.ViewModels.GamePageViewModel>(NavigationTransition.None);
        var state = host.Services.GetRequiredService<GamePresentationState>();
        state.IsDialogueVisible = true;
        state.CompleteOpeningPresentation();
        var page = host.Services.GetRequiredService<GamePage>();
        page.Dialogue.Speaker = "角色名 / Speaker";
        page.Dialogue.CharactersPerSecond = 0;
        page.Dialogue.Text = "保持场景与对白构图。Keep the desktop composition and toolbar unchanged.";
        var window = new Window { Width = 1280, Height = 720, Content = host.View };
        window.Show();
        try
        {
            window.UpdateLayout();
            await page.Dialogue.StartAsync();
            window.UpdateLayout();
            var toolbarButtons = page.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("player-link") && button.IsEffectivelyVisible).ToArray();
            Assert.That(toolbarButtons.Length, Is.EqualTo(9), "History is unavailable in this fixture.");
            var canvas = host.View.FindControl<GameCanvasHost>("GameCanvas")!;
            Assert.That(canvas.GameContent, Is.Null);
            Assert.That(host.Services.GetRequiredService<GameShell>().Bounds.Size, Is.EqualTo(new Size(1280, 720)));
            Assert.That(page.Scene.Bounds.Size, Is.EqualTo(new Size(1920, 1080)));
            Assert.That(page.Dialogue.Bounds.Width, Is.EqualTo(1792));
            var directory = Environment.GetEnvironmentVariable("GALNET_MOBILE_CAPTURE_DIR");
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                using var image = window.CaptureRenderedFrame();
                image!.Save(Path.Combine(directory, "desktop-game.png"));
                var geometry = toolbarButtons.Select(button => new { button.Name, Position = button.TranslatePoint(default, page), button.Bounds.Size });
                File.WriteAllText(Path.Combine(directory, "desktop-game.json"), System.Text.Json.JsonSerializer.Serialize(geometry));
            }
        }
        finally { window.Close(); }
    });
}
