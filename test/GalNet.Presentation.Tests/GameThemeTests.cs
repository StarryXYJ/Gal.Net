using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Avalonia.GameView.Composition;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.Settings;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Avalonia.Themes;
using GalNet.Core.Gallery;
using GalNet.Core.Primitives;
using GalNet.Game.Controls;
using GalNet.Presentation.Abstractions.View;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Presentation;

[NonParallelizable]
public sealed class GameThemeTests
{
    [TestCase("Classic")]
    [TestCase("Minimal")]
    [TestCase("Paper")]
    public async Task SettingTypeTemplateOverridesKeepOtherEditorsAndSharedRows(string layout)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(ThemeTestApplication));
        await session.Dispatch(() =>
        {
            var flag = new BoolSetting("flag", "Flag", false, () => false, (_, _) => Task.CompletedTask);
            var interval = new IntSetting("interval", "Interval", 50, 0, 5000, 10, () => 50, (_, _) => Task.CompletedTask);
            var control = new GalNet.Avalonia.Themes.Common.SettingsCategories { Categories = [new("game", "Game", [flag, interval])] };
            var root = new Border { Classes = { "galnet-player" }, Child = control };
            root.Styles.Add(CreateTheme(layout, "Ocean").CreateStyles());
            var window = new Window { Width = 1280, Height = 720, Content = root };
            window.Show();
            try
            {
                window.UpdateLayout();
                Assert.That(control.GetVisualDescendants().OfType<CheckBox>().Count(), Is.EqualTo(1));
                control.EditorTemplates = [new FuncDataTemplate<BoolSetting>((_, _) => new TextBlock { Text = "Overridden boolean editor" })];
                window.UpdateLayout();
                Assert.That(control.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "Overridden boolean editor"), Is.True);
                Assert.That(control.GetVisualDescendants().OfType<CheckBox>(), Is.Empty);
                Assert.That(control.GetVisualDescendants().OfType<NumericUpDown>().Count(), Is.EqualTo(1));
                control.EditorTemplates = null;
                window.UpdateLayout();
                Assert.That(control.GetVisualDescendants().OfType<CheckBox>().Count(), Is.EqualTo(1));
            }
            finally { window.Close(); }
            return true;
        }, TestContext.CurrentContext.CancellationToken);
    }

    [Test]
    public async Task SettingsTabsHideEmptyCategoriesAndKeepSelectionUntilItBecomesHidden()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(ThemeTestApplication));
        await session.Dispatch(() =>
        {
            var visible = true;
            var item = new BoolSetting("dynamic", "Dynamic", false, () => false, (_, _) => Task.CompletedTask)
            { EvaluateAvailability = () => new(visible) };
            var first = new SettingCategory("first", "First", [new BoolSetting("fixed", "Fixed", false, () => false, (_, _) => Task.CompletedTask)]);
            var second = new SettingCategory("second", "Second", [item]);
            var control = new GalNet.Avalonia.Themes.Common.SettingsCategories { Categories = [first, second, new("empty", "Empty", [])] };
            var window = new Window { Width = 1280, Height = 720, Content = control };
            window.Show();
            try
            {
                var tabs = control.FindControl<TabControl>("CategoryTabs")!;
                Assert.That(control.VisibleCategories, Is.EqualTo(new[] { first, second }));
                tabs.SelectedItem = second;
                item.RefreshAvailability();
                Assert.That(tabs.SelectedItem, Is.SameAs(second));
                visible = false;
                item.RefreshAvailability();
                Assert.That(control.VisibleCategories, Is.EqualTo(new[] { first }));
                Assert.That(tabs.SelectedItem, Is.SameAs(first));
                visible = true;
                item.RefreshAvailability();
                Assert.That(control.VisibleCategories, Is.EqualTo(new[] { first, second }));
                Assert.That(tabs.SelectedItem, Is.SameAs(first));
            }
            finally { window.Close(); }
            return true;
        }, TestContext.CurrentContext.CancellationToken);
    }

    [Test]
    public async Task SkipPromptDisablesBackgroundAndEscapeClosesWithoutSkipping()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(ThemeTestApplication));
        await session.Dispatch(async () =>
        {
            var theme = CreateTheme("Classic", "Ocean");
            using var provider = CreateServices(theme).BuildServiceProvider();
            using var scope = provider.CreateScope();
            var services = scope.ServiceProvider;
            var state = services.GetRequiredService<GamePageViewModel>();
            var controller = new TestPlaybackController();
            state.BindPlayback(controller);
            var navigation = services.GetRequiredService<IGameNavigationService>();
            await navigation.ResetToAsync<GamePageViewModel>(NavigationTransition.None);
            using var shell = services.GetRequiredService<GameShell>();
            var window = new Window { Width = 1280, Height = 720, Content = shell };
            window.Show();
            try
            {
                state.RequestSkipCommand.Execute(null);
                window.UpdateLayout();
                var prompt = shell.FindControl<SkipConfirmation>("SkipPrompt")!;
                Assert.That(prompt.IsVisible, Is.True);
                Assert.That(shell.FindControl<TransitioningContentControl>("PageHost")!.IsEnabled, Is.False);
                Assert.That(prompt.GetVisualDescendants().OfType<Button>().Count(), Is.EqualTo(2));
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                Assert.That(state.IsSkipConfirmationOpen, Is.False);
                Assert.That(controller.Skips, Is.Zero);
                Assert.That(shell.FindControl<TransitioningContentControl>("PageHost")!.IsEnabled, Is.True);
            }
            finally { state.BindPlayback(null); window.Close(); }
            return true;
        }, TestContext.CurrentContext.CancellationToken);
    }

    [Test]
    public async Task PlaybackTogglesPreservePointerSelectionAndSkipRequiresConfirmation([Values("Classic", "Paper", "Minimal")] string layout, [Values("Ocean", "Rose", "Mono")] string palette)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(ThemeTestApplication));
        await session.Dispatch(async () =>
        {
            var theme = CreateTheme(layout, palette);
            using var provider = CreateServices(theme).BuildServiceProvider();
            using var scope = provider.CreateScope();
            var state = scope.ServiceProvider.GetRequiredService<GamePageViewModel>();
            var page = scope.ServiceProvider.GetRequiredService<GamePage>();
            var controller = new TestPlaybackController();
            state.BindPlayback(controller);
            state.IsGameUiReady = true;
            page.DataContext = state;
            var root = new Border { Child = page, Classes = { "galnet-player" } };
            root.Styles.Add(theme.CreateStyles());
            var window = new Window { Width = 1280, Height = 720, Content = root };
            window.Show();
            try
            {
                window.UpdateLayout();
                var quick = page.GetVisualDescendants().OfType<ToggleButton>().Single(button => button.Command == state.ToggleQuickCommand);
                var auto = page.GetVisualDescendants().OfType<ToggleButton>().Single(button => button.Command == state.ToggleAutoCommand);
                var skip = page.GetVisualDescendants().OfType<Button>().Single(button => button.Command == state.RequestSkipCommand);
                Assert.That(((ISolidColorBrush)quick.Background!).Color.A, Is.Zero);
                Assert.That(((ISolidColorBrush)auto.Background!).Color.A, Is.Zero);
                var quickPoint = BoundsInWindow(quick, window).Center;
                window.MouseMove(quickPoint);
                Assert.That(((ISolidColorBrush)quick.Background!).Color.A, Is.Zero, "Unselected modes stay transparent even on hover.");
                var skipPoint = BoundsInWindow(skip, window).Center;
                window.MouseMove(skipPoint);
                var hover = (ISolidColorBrush)skip.Background!;
                Assert.That(hover.Opacity, Is.EqualTo(0.08));
                window.MouseDown(skipPoint, MouseButton.Left);
                Assert.That(((ISolidColorBrush)skip.Background!).Opacity, Is.GreaterThan(hover.Opacity));
                Assert.That(skip.GetVisualDescendants().OfType<ContentPresenter>().First().Background, Is.SameAs(skip.Background));
                window.MouseUp(skipPoint, MouseButton.Left);
                state.CancelSkipCommand.Execute(null);
                Click(window, quick);
                Assert.That(state.IsQuickMode, Is.True);
                Assert.That(quick.IsChecked, Is.True);
                Assert.That(((ISolidColorBrush)quick.Background!).Opacity, Is.EqualTo(0.24));
                Click(window, quick);
                Assert.That(state.IsQuickMode, Is.False, "Tunnel cancellation must not turn an off-toggle into an on-toggle.");
                Assert.That(quick.IsChecked, Is.False, "The button must also clear its own toggled visual state.");
                Assert.That(((ISolidColorBrush)quick.Background!).Color.A, Is.Zero);
                Click(window, quick);
                Click(window, auto);
                Assert.That(state.IsAutoMode, Is.True);
                Assert.That(state.IsQuickMode, Is.False);
                Assert.That(auto.IsChecked, Is.True);
                Click(window, auto);
                Assert.That(state.IsAutoMode, Is.False);
                Assert.That(auto.IsChecked, Is.False);
                Click(window, auto);
                state.ClearPlaybackPointer();
                await state.ToggleAutoCommand.ExecuteAsync(null);
                Assert.That(state.IsAutoMode, Is.False, "Keyboard toggle uses current state rather than a stale pointer snapshot.");
                state.RequestSkipCommand.Execute(null);
                Assert.That(state.IsSkipConfirmationOpen, Is.True);
                Assert.That(controller.Skips, Is.Zero);
                var advances = 0;
                state.AdvanceRequested += () => advances++;
                state.AdvanceCommand.Execute(null);
                Assert.That(advances, Is.Zero);
                state.CancelSkipCommand.Execute(null);
                Assert.That(controller.Skips, Is.Zero);
                state.RequestSkipCommand.Execute(null);
                await state.ConfirmSkipCommand.ExecuteAsync(null);
                Assert.That(controller.Skips, Is.EqualTo(1));
                Assert.That(state.IsSkipConfirmationOpen, Is.False);
                window.MouseDown(new Point(100, 100), MouseButton.Right);
                window.MouseUp(new Point(100, 100), MouseButton.Right);
                Assert.That(advances, Is.Zero);
            }
            finally { state.BindPlayback(null); window.Close(); }
            return true;
        }, TestContext.CurrentContext.CancellationToken);
    }

    private sealed class TestPlaybackController : IPlayerPlaybackController
    {
        public PlaybackMode PlaybackMode { get; private set; }
        public event Action<PlaybackMode>? PlaybackModeChanged;
        public int Skips { get; private set; }
        public void CancelPlayback() { }
        public Task SetPlaybackModeAsync(PlaybackMode mode, CancellationToken cancellationToken = default)
        {
            PlaybackMode = mode;
            PlaybackModeChanged?.Invoke(mode);
            return Task.CompletedTask;
        }
        public Task SkipToUnreadAsync(CancellationToken cancellationToken = default) { Skips++; return Task.CompletedTask; }
        public Task RefreshPlaybackSettingsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Test]
    public async Task CustomSettingsReleaseEditorsOnReplacementAndDetach()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(ThemeTestApplication));
        await session.Dispatch(() =>
        {
            var created = 0;
            var released = 0;
            var setting = new CustomSetting<string>("custom", "Custom", "", () => "", (_, _) => Task.CompletedTask,
                _ => { created++; return new SettingEditorLease(new TextBox(), () => released++); });
            var editor = new CustomSettingEditor { DataContext = setting };
            var window = new Window { Content = editor };
            window.Show();
            window.UpdateLayout();
            Assert.That(created, Is.EqualTo(1));
            var original = editor.Content;
            editor.DataContext = null;
            Assert.That(released, Is.EqualTo(1));
            editor.DataContext = setting;
            Assert.That(created, Is.EqualTo(2));
            Assert.That(editor.Content, Is.Not.SameAs(original));
            window.Close();
            Assert.That(released, Is.EqualTo(2));
            return true;
        }, TestContext.CurrentContext.CancellationToken);
    }

    private static readonly string[] GalleryTypeLabels = ["CG", "Video", "Audio"];
    [TestCase("Classic", "Ocean")]
    [TestCase("Classic", "Rose")]
    [TestCase("Classic", "Mono")]
    [TestCase("Minimal", "Ocean")]
    [TestCase("Minimal", "Rose")]
    [TestCase("Minimal", "Mono")]
    [TestCase("Paper", "Ocean")]
    [TestCase("Paper", "Rose")]
    [TestCase("Paper", "Mono")]
    public async Task Every_layout_palette_combination_renders_and_preserves_game_interaction(string layout, string paletteName)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(ThemeTestApplication));
        await session.Dispatch(async () =>
        {
            var theme = CreateTheme(layout, paletteName);
            using var provider = CreateServices(theme).BuildServiceProvider();
            using var scope = provider.CreateScope();
            var services = scope.ServiceProvider;
            var factory = services.GetRequiredService<IPageViewFactory>();
            var title = factory.Create(services.GetRequiredService<TitlePageViewModel>());
            var root = new Border { Classes = { "galnet-player" }, Child = title };
            root.Styles.Add(theme.CreateStyles());
            root.TryFindResource("GalNet.Brush.Background", out var rootBackground);
            root.Background = (IBrush?)rootBackground;
            var window = new Window { Width = 1280, Height = 720, Content = root };
            window.Show();
            try
            {
                window.UpdateLayout();
                Assert.That(title.GetVisualDescendants().OfType<Button>().Count(), Is.GreaterThanOrEqualTo(4));
                AssertPalette(root, theme.Palette);
                Capture(window, $"{layout}-{paletteName}-title");

                var state = services.GetRequiredService<GamePageViewModel>();
                var page = services.GetRequiredService<GamePage>();
                page.DataContext = state;
                root.Child = page;
                state.IsGameUiReady = true;
                state.IsDialogueVisible = true;
                window.UpdateLayout();
                page.EnsureLayout();
                Assert.That(page.SceneHost.ItemsSource, Is.SameAs(state.Layers));
                Assert.That(page.GetVisualDescendants().Contains(page.Dialogue), Is.True);

                state.TextSpeed = 0;
                using var presenter = new AvaloniaDialoguePresenter(state, page, AvaloniaUiDispatcher.Instance);
                await presenter.StartTypewriter("default_dialogue", "Aster",
                    "A theme changes the layout; the story keeps its state.", TestContext.CurrentContext.CancellationToken);
                Assert.That(page.Dialogue.GetVisualDescendants().OfType<TypewriterTextBlock>().Single().FontSize,
                    Is.EqualTo(layout == "Minimal" ? 20 : 22));
                Assert.That(page.Dialogue.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Text == "Aster").FontSize,
                    Is.EqualTo(layout == "Minimal" ? 24 : 26));
                Capture(window, $"{layout}-{paletteName}-game");

                var advances = 0;
                state.AdvanceRequested += () =>
                {
                    advances++;
                    state.CompleteAdvance();
                };
                var advanceWait = presenter.WaitForClickAsync(TestContext.CurrentContext.CancellationToken);
                Assert.That(advanceWait.IsCompleted, Is.False, "The first dialogue must wait for player input.");
                Click(window, page.Scene);
                await advanceWait.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.That(advances, Is.EqualTo(1));
                advances = 0;
                var hideButton = page.GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.Command, state.HideUiCommand));
                Click(window, hideButton);
                Assert.That(state.IsUiHidden, Is.True);
                Assert.That(advances, Is.Zero, "Toolbar interaction must not advance the story.");
                state.IsUiHidden = false;

                var choiceWait = presenter.WaitForChoiceAsync("default_choice", ["First option", "Second option"],
                    TestContext.CurrentContext.CancellationToken);
                window.UpdateLayout();
                Assert.That(page.Choice.ItemsSource, Is.SameAs(state.Choices));
                Capture(window, $"{layout}-{paletteName}-choices");
                Click(window, page.Choice.ContainerFromIndex(1)!);
                Assert.That(await choiceWait.WaitAsync(TimeSpan.FromSeconds(2)), Is.EqualTo(1));
                Assert.That(advances, Is.Zero, "Choice input must not also advance the story.");

                state.IsNvlMode = true;
                await presenter.AppendNvlTextAsync("The first remembered line.", TestContext.CurrentContext.CancellationToken);
                await presenter.AppendNvlTextAsync("The second remembered line.", TestContext.CurrentContext.CancellationToken);
                var nvl = page.Nvl;
                root.Child = new TextBlock { Text = "Another page" };
                root.Child = page;
                window.UpdateLayout();
                Assert.That(page.Nvl, Is.SameAs(nvl));
                Assert.That(nvl.GetVisualDescendants().OfType<NvlLinePresenter>().Count(), Is.EqualTo(2));
                Capture(window, $"{layout}-{paletteName}-nvl");

                await presenter.BeginCatchUpAsync();
                presenter.RecordDialogue("Aster", "Skipped\\d{60000}\\skip text is projected without intermediate rendering.");
                presenter.HideDialogue();
                presenter.ShowNvl();
                presenter.ClearNvl();
                presenter.RecordNvlParagraph("Final\\d{60000}\\skip NVL history.");
                Assert.That(nvl.GetVisualDescendants().OfType<NvlLinePresenter>().Count(), Is.EqualTo(2), "Catch-up must not render intermediate clears/appends.");
                await presenter.EndCatchUpAsync(TestContext.CurrentContext.CancellationToken).WaitAsync(TimeSpan.FromSeconds(2));
                window.UpdateLayout();
                Assert.That(page.Dialogue.Text, Is.EqualTo("Skipped\\d{60000}\\skip text is projected without intermediate rendering."));
                Assert.That(state.IsDialogueVisible, Is.False);
                Assert.That(state.IsNvlMode, Is.True);
                Assert.That(nvl.GetVisualDescendants().OfType<NvlLinePresenter>().Single().Text, Is.EqualTo("Final\\d{60000}\\skip NVL history."));

                using var image = new Bitmap(new MemoryStream(page.CaptureScenePng()));
                Assert.That(image.PixelSize.Width, Is.GreaterThan(0));

                root.Child = factory.Create(services.GetRequiredService<SettingsPageViewModel>());
                window.UpdateLayout();
                var settingsTabs = root.Child.GetVisualDescendants().OfType<TabControl>().Single();
                Assert.That(settingsTabs.ItemCount, Is.EqualTo(1));
                Assert.That(root.Child.GetVisualDescendants().OfType<Slider>().Any(slider => slider.IsVisible), Is.True);
                var settingsFrame = root.Child.FindControl<Border>("PageFrame")!;
                var settingsBack = root.Child.FindControl<Button>("PageBackButton")!;
                var frameBounds = BoundsInWindow(settingsFrame, window);
                var backBounds = BoundsInWindow(settingsBack, window);
                Capture(window, $"{layout}-{paletteName}-settings");

                foreach (var viewModelType in new[]
                {
                    typeof(SaveSlotsPageViewModel), typeof(AboutPageViewModel), typeof(LoadingPageViewModel),
                    typeof(GalleryPageViewModel), typeof(CgGalleryPageViewModel), typeof(CgGalleryImagePageViewModel),
                    typeof(AudioGalleryPageViewModel), typeof(VideoGalleryPageViewModel),
                    typeof(VideoGalleryPlayerPageViewModel), typeof(MissingGalleryPageViewModel)
                })
                {
                    var viewModel = (PageViewModelBase)services.GetRequiredService(viewModelType);
                    if (viewModel is GalleryPageViewModel galleryIndex)
                    {
                        foreach (var typeId in GalleryTypeLabels)
                            galleryIndex.Types.Add(new GalleryTypeData(
                                new GalleryTypeRegistration { TypeId = typeId, ResourceTypeId = typeId.ToLowerInvariant() }, []));
                    }
                    if (viewModel is AudioGalleryPageViewModel audioGallery)
                    {
                        audioGallery.Items.Add(new GalleryEntryViewModel(new GalleryItemData(
                            new GalleryItem { Id = 1, Title = "Evening melody / 暮色之声" }, true)));
                        audioGallery.Items.Add(new GalleryEntryViewModel(new GalleryItemData(
                            new GalleryItem { Id = 2, Title = "An undiscovered melody" }, false)));
                    }
                    if (viewModel is GalleryVisualPageViewModelBase visualGallery)
                    {
                        visualGallery.Items.Add(new GalleryEntryViewModel(new GalleryItemData(
                            new GalleryItem { Id = 1, Title = "Evening light / 暮色" }, true))
                        { Thumbnail = CreateGalleryArtwork() });
                        visualGallery.Items.Add(new GalleryEntryViewModel(new GalleryItemData(
                            new GalleryItem { Id = 2, Title = "An undiscovered memory" }, false)));
                    }
                    if (viewModel is CgGalleryImagePageViewModel imagePage)
                    {
                        imagePage.Title = "Evening light / 暮色";
                        imagePage.Image = CreateGalleryArtwork();
                    }
                    root.Child = factory.Create(viewModel);
                    Assert.That(root.Child.GetType().Namespace, Is.EqualTo($"GalNet.Avalonia.Themes.Layouts.{layout}"));
                    window.UpdateLayout();
                    if (root.Child.FindControl<Border>("PageFrame") is { } frame)
                    {
                        Assert.That(BoundsInWindow(frame, window), Is.EqualTo(frameBounds), viewModelType.Name);
                        Assert.That(frame.BorderThickness, Is.EqualTo(default(Thickness)), "Pages must not have an enclosing outline.");
                        Assert.That(BoundsInWindow(root.Child.FindControl<Button>("PageBackButton")!, window),
                            Is.EqualTo(backBounds), "The return target must stay in the same place across pages.");
                    }
                    Capture(window, $"{layout}-{paletteName}-{viewModelType.Name}");
                }

                using var screenshotDialog = (ScreenshotDialog)ActivatorUtilities.CreateInstance(services,
                    services.GetRequiredService<IPageViewRegistry>().GetViewType(typeof(ScreenshotDialogViewModel)));
                root.Child = screenshotDialog;
                window.UpdateLayout();
                Capture(window, $"{layout}-{paletteName}-screenshot-dialog");
            }
            finally { window.Close(); }
            return true;
        }, TestContext.CurrentContext.CancellationToken);
    }

    [TestCase("Classic", 1280, 720)]
    [TestCase("Minimal", 1280, 720)]
    [TestCase("Paper", 1280, 720)]
    [TestCase("Classic", 800, 500)]
    [TestCase("Minimal", 800, 500)]
    [TestCase("Paper", 800, 500)]
    public async Task Large_gallery_images_keep_the_return_target_visible_and_clickable(string layout, int width, int height)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(ThemeTestApplication));
        await session.Dispatch(() =>
        {
            var theme = CreateTheme(layout, "Ocean");
            using var provider = CreateServices(theme).BuildServiceProvider();
            using var scope = provider.CreateScope();
            var services = scope.ServiceProvider;
            var navigation = services.GetRequiredService<IGameNavigationService>();
            var state = services.GetRequiredService<CgGalleryImagePageViewModel>();
            state.Title = "A very long Gallery image title / 长标题也不能挤占返回按钮";
            state.Image = CreateGalleryArtwork();
            navigation.ResetTo<SettingsPageViewModel>(NavigationTransition.None);
            navigation.Navigate<CgGalleryImagePageViewModel>(NavigationTransition.None);
            var page = services.GetRequiredService<IPageViewFactory>().Create(state);
            var root = new Border { Classes = { "galnet-player" }, Child = page };
            root.Styles.Add(theme.CreateStyles());
            var window = new Window { Width = width, Height = height, Content = root };
            window.Show();
            try
            {
                window.UpdateLayout();
                var back = page.FindControl<Button>("PageBackButton")!;
                var media = page.FindControl<Image>("PageMedia")!;
                var backRect = BoundsInWindow(back, window);
                var mediaRect = BoundsInWindow(media, window);
                Assert.That(new Rect(window.ClientSize).Contains(backRect), Is.True);
                Assert.That(new Rect(window.ClientSize).Contains(mediaRect), Is.True);
                Assert.That(mediaRect.Intersects(backRect), Is.False, "The media must not cover the return target.");
                Assert.That(backRect.Width, Is.GreaterThanOrEqualTo(120));
                Assert.That(backRect.Height, Is.GreaterThanOrEqualTo(48));
                Capture(window, $"{layout}-large-image-{width}x{height}");
                Click(window, back);
                Assert.That(navigation.CurrentViewModel, Is.TypeOf<SettingsPageViewModel>());
            }
            finally { window.Close(); }
        }, TestContext.CurrentContext.CancellationToken);
    }

    [Test]
    public void Theme_then_host_overrides_are_registered_without_manual_DI_view_registration()
    {
        var order = new List<string>();
        var theme = new RecordingTheme(order);
        using var provider = CreateServices(theme, views =>
        {
            order.Add("host");
            views.Register<TitlePageViewModel, HostTitle>();
        }).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;
        var registry = services.GetRequiredService<IPageViewRegistry>();
        var factory = services.GetRequiredService<IPageViewFactory>();
        var state = services.GetRequiredService<TitlePageViewModel>();
        var firstView = factory.Create(state);
        var secondView = factory.Create(state);

        Assert.Multiple(() =>
        {
            Assert.That(string.Join(",", order), Is.EqualTo("theme,host"));
            Assert.That(registry.GetViewType(typeof(TitlePageViewModel)), Is.EqualTo(typeof(HostTitle)));
            Assert.That(factory.Create(state), Is.TypeOf<HostTitle>());
            Assert.That(secondView, Is.SameAs(firstView));
            Assert.That(registry.GetViewType(typeof(SettingsPageViewModel)), Is.EqualTo(typeof(global::GalNet.Avalonia.Themes.Layouts.Classic.ClassicSettingsPage)));
        });
    }

    [Test]
    public void A_custom_game_page_is_the_same_instance_used_by_the_runtime_and_the_view_factory()
    {
        using var provider = CreateServices(new ClassicTheme(), views => views.Register<GamePageViewModel, HostGamePage>()).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;
        var view = services.GetRequiredService<IPageViewFactory>().Create(services.GetRequiredService<GamePageViewModel>());
        Assert.That(view, Is.TypeOf<HostGamePage>());
        Assert.That(services.GetRequiredService<GamePage>(), Is.SameAs(view));
    }

    [Test]
    public void A_game_view_without_the_game_host_contract_is_rejected_during_registration()
    {
        Assert.Throws<InvalidOperationException>(() => CreateServices(new ClassicTheme(),
            views => views.Register<GamePageViewModel, HostTitle>()));
    }

    [TestCase("Classic")]
    [TestCase("Minimal")]
    [TestCase("Paper")]
    public async Task The_shared_shell_resolves_the_theme_and_navigates_to_theme_owned_pages(string layout)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(ThemeTestApplication));
        await session.Dispatch(async () =>
        {
            using var provider = CreateServices(CreateTheme(layout, "Ocean")).BuildServiceProvider(
                new ServiceProviderOptions { ValidateScopes = true });
            using var scope = provider.CreateScope();
            var services = scope.ServiceProvider;
            var navigation = services.GetRequiredService<IGameNavigationService>();
            navigation.ResetTo<TitlePageViewModel>();
            var shell = services.GetRequiredService<GameShell>();
            var window = new Window { Width = 1280, Height = 720, Content = shell };
            window.Show();
            try
            {
                window.UpdateLayout();
                var host = shell.FindControl<TransitioningContentControl>("PageHost")!;
                Assert.That(host.Content!.GetType().Namespace, Is.EqualTo($"GalNet.Avalonia.Themes.Layouts.{layout}"));
                Assert.That(shell.Background, Is.Not.Null);
                await navigation.NavigateAsync(services.GetRequiredService<SettingsPageViewModel>(), NavigationTransition.None);
                window.UpdateLayout();
                Assert.That(host.Content, Is.SameAs(services.GetRequiredService<IPageViewFactory>()
                    .Create(services.GetRequiredService<SettingsPageViewModel>())));
                Assert.That(((Control)host.Content!).FindControl<Button>("PageBackButton"), Is.Not.Null);
            }
            finally { window.Close(); }
        }, TestContext.CurrentContext.CancellationToken);
    }

    [Test]
    public void An_incomplete_theme_reports_all_missing_views_during_registration()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CreateServices(new IncompleteTheme()));
        Assert.That(exception!.Message, Does.Contain(nameof(SettingsPageViewModel)));
        Assert.That(exception.Message, Does.Contain(nameof(ScreenshotDialogViewModel)));
    }

    [Test]
    public void A_screenshot_view_without_the_behavior_contract_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => CreateServices(new ClassicTheme(),
            views => views.Register<ScreenshotDialogViewModel, HostTitle>()));
    }

    [TestCase("Classic")]
    [TestCase("Minimal")]
    [TestCase("Paper")]
    public async Task Screenshot_layouts_preserve_preview_save_errors_cancel_and_cleanup(string layout)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(ThemeTestApplication));
        await session.Dispatch(() =>
        {
            var theme = CreateTheme(layout, "Ocean");
            using var provider = CreateServices(theme).BuildServiceProvider();
            var type = provider.GetRequiredService<IPageViewRegistry>().GetViewType(typeof(ScreenshotDialogViewModel));
            using var dialog = (ScreenshotDialog)ActivatorUtilities.CreateInstance(provider, type);
            dialog.Styles.Add(theme.CreateStyles());
            using var artwork = CreateGalleryArtwork();
            using var stream = new MemoryStream();
            artwork.Save(stream);
            var bytes = stream.ToArray();
            var captureFlags = new List<bool>();
            var saves = new List<(string Directory, string File, bool IncludeUi)>();
            var outcomes = new List<object?>();
            var model = new ScreenshotDialogViewModel("original", "original.png", includeUi =>
            {
                captureFlags.Add(includeUi);
                return Task.FromResult(bytes);
            }, (directory, file, includeUi) =>
            {
                saves.Add((directory, file, includeUi));
                return Task.FromResult<string?>(saves.Count == 1 ? "destination unavailable" : null);
            });
            model.RequestClose += (_, outcome) => outcomes.Add(outcome);
            dialog.DataContext = model;
            var window = new Window { Width = 1000, Height = 850, Content = dialog };
            window.Show();
            try
            {
                window.UpdateLayout();
                var preview = dialog.FindControl<Image>("Preview")!;
                Assert.That(preview.Source, Is.Not.Null);
                Click(window, dialog.FindControl<Button>("CancelButton")!);
                Assert.That(outcomes.Single(), Is.EqualTo(false));
                outcomes.Clear();
                Click(window, dialog.FindControl<CheckBox>("IncludeUiBox")!);
                Assert.That(captureFlags, Has.Count.EqualTo(2));
                Assert.That(captureFlags[0], Is.False);
                Assert.That(captureFlags[1], Is.True);
                dialog.FindControl<TextBox>("DirectoryBox")!.Text = "chosen";
                dialog.FindControl<TextBox>("FileNameBox")!.Text = "chosen.png";
                Click(window, dialog.FindControl<Button>("SaveButton")!);
                Assert.That(saves[0], Is.EqualTo(("chosen", "chosen.png", true)));
                Assert.That(dialog.FindControl<TextBlock>("ErrorBlock")!.Text, Is.EqualTo("destination unavailable"));
                Assert.That(outcomes, Is.Empty);
                model.RequestClose += (_, outcome) => { if (Equals(outcome, true)) dialog.Dispose(); };
                Click(window, dialog.FindControl<Button>("SaveButton")!);
                Assert.That(outcomes.Single(), Is.EqualTo(true));
                Assert.That(preview.Source, Is.Null);
                Click(window, dialog.FindControl<Button>("CancelButton")!);
                Assert.That(outcomes.Count, Is.EqualTo(1));
            }
            finally { window.Close(); }
        }, TestContext.CurrentContext.CancellationToken);
    }

    [Test]
    public async Task A_screenshot_layout_missing_required_controls_fails_clearly()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(ThemeTestApplication));
        await session.Dispatch(() =>
        {
            var exception = Assert.Throws<InvalidOperationException>(() => new IncompleteScreenshotDialog());
            Assert.That(exception!.Message, Does.Contain("Preview"));
        }, TestContext.CurrentContext.CancellationToken);
    }
    [Test]
    public async Task Missing_game_layout_parts_fail_instead_of_completing_a_blocking_dialogue()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(ThemeTestApplication));
        await session.Dispatch(() =>
        {
            var page = new GamePage { Template = new FuncControlTemplate<GamePage>((_, _) => new Grid()) };
            Assert.Throws<InvalidOperationException>(() => page.EnsureLayout());
        }, TestContext.CurrentContext.CancellationToken);
    }

    [Test]
    public async Task Player_roots_have_independent_palettes_and_host_resources_win()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(ThemeTestApplication));
        await session.Dispatch(() =>
        {
            var left = new Border { Classes = { "galnet-player" }, Child = new Button { Content = "Ocean" } };
            var right = new Border { Classes = { "galnet-player" }, Child = new Button { Content = "Mono" } };
            left.Styles.Add(new ClassicTheme(GamePalette.Ocean).CreateStyles());
            right.Styles.Add(new ClassicTheme(GamePalette.Mono).CreateStyles());
            var window = new Window { Content = new StackPanel { Children = { left, right } } };
            window.Show();
            try
            {
                window.UpdateLayout();
                left.TryFindResource("GalNet.Brush.Background", out var ocean);
                right.TryFindResource("GalNet.Brush.Background", out var mono);
                Assert.That(((SolidColorBrush)ocean!).Color, Is.Not.EqualTo(((SolidColorBrush)mono!).Color));
                left.Resources["GalNet.Brush.Surface"] = Brushes.Magenta;
                window.UpdateLayout();
                Assert.That(((Button)left.Child!).Background, Is.SameAs(Brushes.Magenta));
                Assert.That(((Button)right.Child!).Background, Is.Not.SameAs(Brushes.Magenta));
            }
            finally { window.Close(); }
        }, TestContext.CurrentContext.CancellationToken);
    }

    private static void AssertPalette(Control root, GamePalette palette)
    {
        var resources = palette.CreateResources();
        foreach (var name in GamePalette.RequiredBrushes)
        {
            var key = $"GalNet.Brush.{name}";
            Assert.That(root.TryFindResource(key, out var actual), Is.True, key);
            resources.TryGetResource($"GalNet.Palette.{name}Color", ThemeVariant.Default, out var expected);
            Assert.That(((SolidColorBrush)actual!).Color, Is.EqualTo(expected), key);
        }
        Color ColorOf(string name)
        {
            resources.TryGetResource($"GalNet.Palette.{name}Color", ThemeVariant.Default, out var color);
            return (Color)color!;
        }
        foreach (var background in new[] { "Primary", "PrimaryHover", "PrimaryPressed" })
            Assert.That(Contrast(ColorOf("OnPrimary"), ColorOf(background)), Is.GreaterThanOrEqualTo(4.5), background);
        Assert.That(Contrast(ColorOf("TextPrimary"), ColorOf("Surface")), Is.GreaterThanOrEqualTo(4.5));
    }

    private static double Contrast(Color foreground, Color background)
    {
        static double Linear(byte value)
        {
            var channel = value / 255d;
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }
        static double Luminance(Color color) => 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        var first = Luminance(foreground);
        var second = Luminance(background);
        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }

    private static void Capture(Window window, string name)
    {
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var bitmap = window.CaptureRenderedFrame();
        Assert.That(bitmap, Is.Not.Null);
        var directory = Environment.GetEnvironmentVariable("GALNET_THEME_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        bitmap!.Save(Path.Combine(directory, $"{name}.png"));
    }

    private static void Click(Window window, Control target)
    {
        window.UpdateLayout();
        var point = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("The interactive control is not attached to the test window.");
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }

    private static Rect BoundsInWindow(Control control, Window window) =>
        new(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);

    private static RenderTargetBitmap CreateGalleryArtwork()
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(3840, 2160));
        using var drawing = bitmap.CreateDrawingContext();
        drawing.DrawRectangle(new SolidColorBrush(Color.Parse("#213C56")), null, new Rect(0, 0, 3840, 2160));
        drawing.DrawEllipse(new SolidColorBrush(Color.Parse("#EDCC9D")), null, new Point(2850, 650), 340, 340);
        drawing.DrawRectangle(new SolidColorBrush(Color.Parse("#3E706C")), null, new Rect(0, 1350, 3840, 810));
        drawing.DrawRectangle(new SolidColorBrush(Color.Parse("#254D53")), null, new Rect(0, 1650, 3840, 510));
        return bitmap;
    }

    private static GameLayoutTheme CreateTheme(string layout, string paletteName)
    {
        var palette = paletteName switch { "Ocean" => GamePalette.Ocean, "Rose" => GamePalette.Rose, _ => GamePalette.Mono };
        return layout switch { "Classic" => new ClassicTheme(palette), "Minimal" => new MinimalTheme(palette), _ => new PaperTheme(palette) };
    }

    private static ServiceCollection CreateServices(IGameLayoutTheme theme, Action<IPageViewRegistryBuilder>? overrides = null)
    {
        var services = new ServiceCollection();
        services.AddAvaloniaGameViewPages(theme, configureViews: overrides);
        services.AddScoped<IGameSessionService, TestSession>();
        return services;
    }

    private sealed class RecordingTheme(List<string> order) : IGameLayoutTheme
    {
        public IStyle CreateStyles() => new Styles();
        public void RegisterViews(IPageViewRegistryBuilder views)
        {
            order.Add("theme");
            new ClassicTheme().RegisterViews(views);
            views.Register<TitlePageViewModel, ThemeTitle>();
        }
    }

    private sealed class IncompleteTheme : IGameLayoutTheme
    {
        public IStyle CreateStyles() => new Styles();
        public void RegisterViews(IPageViewRegistryBuilder views) => views.Register<TitlePageViewModel, ThemeTitle>();
    }

    private sealed class ThemeTitle : Control;
    private sealed class IncompleteScreenshotDialog : ScreenshotDialog
    {
        public IncompleteScreenshotDialog() => InitializeDialog();
    }
    private sealed class HostTitle : Control;
    private sealed class HostGamePage : GamePage;

    private sealed class TestSession : IGameSessionService
    {
        public string GameTitle => "Aster / 星河";
        public string StatusMessage => "A visual novel built with GalNet";
        public bool IsReady => true;
        public bool IsPlaying => false;
        public bool CanContinue => true;
        public ReadOnlyObservableCollection<GameSaveSlot> SaveSlots { get; } = new(new ObservableCollection<GameSaveSlot>
        {
            new(0, default, "", true, false),
            new(1, new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc), "Chapter one / 第一章", false, false),
            new(2, default, "Unavailable save", false, true)
        });
        public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
        public Task StartNewGameAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ContinueAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveAsync(int slotIndex, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(int slotIndex, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

public static class ThemeTestApplication
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Application>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
