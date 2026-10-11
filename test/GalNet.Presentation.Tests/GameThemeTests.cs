using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
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
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Avalonia.Themes;
using GalNet.Core.Gallery;
using GalNet.Game.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Presentation;

[NonParallelizable]
public sealed class GameThemeTests
{
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

                using var image = new Bitmap(new MemoryStream(page.CaptureScenePng()));
                Assert.That(image.PixelSize.Width, Is.GreaterThan(0));

                root.Child = factory.Create(services.GetRequiredService<SettingsPageViewModel>());
                window.UpdateLayout();
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

                root.Child = new ScreenshotDialog();
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
            Assert.That(registry.GetViewType(typeof(SettingsPageViewModel)), Is.EqualTo(typeof(SettingsPage)));
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

    [Test]
    public async Task The_default_layout_remains_usable_without_the_optional_themes_project()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(ThemeTestApplication));
        await session.Dispatch(() =>
        {
            var page = new GamePage();
            var root = new Border { Classes = { "galnet-player" }, Child = page };
            root.Styles.Add(new global::Avalonia.Themes.Fluent.FluentTheme());
            root.Styles.Add(new global::Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://GalNet.Avalonia.GameView/"))
            {
                Source = new Uri("avares://GalNet.Avalonia.GameView/Styles/Generic.axaml")
            });
            var window = new Window { Width = 1280, Height = 720, Content = root };
            window.Show();
            try
            {
                window.UpdateLayout();
                page.EnsureLayout();
                Assert.That(page.SceneHost.Bounds.Width, Is.EqualTo(1280));
                Assert.That(page.SceneHost.Bounds.Height, Is.EqualTo(720));
                Assert.That(root.TryFindResource("GalNet.Brush.TextPrimary", out _), Is.True);
                Capture(window, "default-layout");
            }
            finally { window.Close(); }
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
        services.AddAvaloniaGameViewPages(overrides, theme: theme);
        services.AddScoped<IGameSessionService, TestSession>();
        return services;
    }

    private sealed class RecordingTheme(List<string> order) : IGameLayoutTheme
    {
        public IStyle CreateStyles() => new Styles();
        public void RegisterViews(IPageViewRegistryBuilder views)
        {
            order.Add("theme");
            views.Register<TitlePageViewModel, ThemeTitle>();
        }
    }

    private sealed class ThemeTitle : Control;
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
