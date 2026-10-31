using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Avalonia.Themes.Common;
using GalNet.Contracts.Builtins.Gallery;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Presentation;

public sealed partial class GameThemeTests
{
    [TestCase("Classic")]
    [TestCase("Minimal")]
    [TestCase("Paper")]
    public async Task AudioGalleryRowsKeepNamesTooltipsAndControlsReadableAtWideAndNarrowSizes(string layout)
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly);
        await session.Dispatch(async () =>
        {
            var theme = CreateTheme(layout, "Mono");
            await using var provider = CreateServices(theme).BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var page = scope.ServiceProvider.GetRequiredService<AudioGalleryPageViewModel>();
            const string name = "A long music title / 很长的曲名，用来检查省略显示和完整提示，而不会挤压播放按钮";
            var entry = new GalleryEntryViewModel(new(new GalleryItem { Id = 1 }, true), name);
            page.Items.Add(entry); page.Items.Add(new(new(new GalleryItem { Id = 2 }, false), "Hidden spoiler"));
            var view = scope.ServiceProvider.GetRequiredService<IPageViewFactory>().Create(page);
            var root = new Border { Classes = { "galnet-player" }, Child = view }; root.Styles.Add(theme.CreateStyles());
            var window = new Window { Width = 1000, Height = 600, Content = root }; window.Show();
            try
            {
                window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var rows = view.GetVisualDescendants().OfType<AudioGalleryRow>().ToArray();
                Assert.That(rows, Has.Length.EqualTo(2));
                var title = rows[0].FindControl<TextBlock>("TrackName")!;
                Assert.That(title.Text, Is.EqualTo(name)); Assert.That(title.TextTrimming, Is.EqualTo(TextTrimming.CharacterEllipsis));
                Assert.That(ToolTip.GetTip(title), Is.EqualTo(name));
                Assert.That(rows[0].ToggleCommand, Is.SameAs(page.ToggleAudioCommand));
                entry.TimeText = "01:23 / 04:56";
                window.UpdateLayout();
                var time = rows[0].FindControl<TextBlock>("TrackTime")!;
                Assert.That(time.Text, Is.EqualTo("01:23 / 04:56"));
                Assert.That(time.FontSize, Is.LessThanOrEqualTo(12));
                Assert.That(time.HorizontalAlignment, Is.EqualTo(global::Avalonia.Layout.HorizontalAlignment.Right));
                Assert.That(Grid.GetRow(rows[0].FindControl<Grid>("Timeline")!), Is.Zero);
                Assert.That(((GalleryEntryViewModel)rows[1].DataContext!).Name, Is.Empty);
                Assert.That(rows[1].FindControl<TextBlock>("TrackName")!.Text, Is.EqualTo("???"));
                Capture(window, $"{layout}-music-gallery-wide");
                window.Width = 480;
                await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Background);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(); window.UpdateLayout();
                Assert.That(Grid.GetRow(rows[0].FindControl<Grid>("Timeline")!), Is.EqualTo(1), $"Window={window.ClientSize}, row={rows[0].Bounds}");
                Capture(window, $"{layout}-music-gallery-narrow");
            }
            finally { window.Close(); }
        }, TestContext.CurrentContext.CancellationToken);
    }
    [TestCase("Classic")]
    [TestCase("Minimal")]
    [TestCase("Paper")]
    public async Task SceneAndImageGalleryHaveIndependentViewsAndLockedNamesStayHidden(string layout)
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly);
        await session.Dispatch(async () =>
        {
            var theme = CreateTheme(layout, "Ocean");
            await using var provider = CreateServices(theme).BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var factory = scope.ServiceProvider.GetRequiredService<IPageViewFactory>();
            var images = scope.ServiceProvider.GetRequiredService<CgGalleryPageViewModel>();
            var scenes = scope.ServiceProvider.GetRequiredService<SceneGalleryPageViewModel>();
            scenes.Items.Add(new(new(new GalleryItem { Id = 400 }, true), "A scene memory / 场景回忆"));
            scenes.Items.Add(new(new(new GalleryItem { Id = 401 }, false), "Spoiler"));
            var imageView = factory.Create(images); var sceneView = factory.Create(scenes);
            Assert.That(sceneView, Is.Not.SameAs(imageView)); Assert.That(imageView.DataContext, Is.SameAs(images));
            Assert.That(scope.ServiceProvider.GetRequiredService<IGalleryPageRegistry>().TryGetViewModelType("scene", out var type), Is.True);
            Assert.That(type, Is.EqualTo(typeof(SceneGalleryPageViewModel)));
            var root = new Border { Classes = { "galnet-player" }, Child = sceneView }; root.Styles.Add(theme.CreateStyles());
            var window = new Window { Width = 1000, Height = 600, Content = root }; window.Show();
            try { window.UpdateLayout(); Assert.That(scenes.Items[1].HasName, Is.False); Assert.That(scenes.Items[1].Title, Is.EqualTo("???")); Capture(window, $"{layout}-scene-gallery"); }
            finally { window.Close(); }
        }, TestContext.CurrentContext.CancellationToken);
    }
    [TestCase("Classic")]
    [TestCase("Minimal")]
    [TestCase("Paper")]
    public async Task GalleryCategoriesAreCenteredInOneColumnAndVideoControlsFollowPlaybackState(string layout)
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly);
        await session.Dispatch(async () =>
        {
            var theme = CreateTheme(layout, "Mono");
            await using var provider = CreateServices(theme).BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var factory = scope.ServiceProvider.GetRequiredService<IPageViewFactory>();
            var index = scope.ServiceProvider.GetRequiredService<GalleryPageViewModel>();
            foreach (var type in new[] { "cg", "video", "audio", "scene" })
                index.Types.Add(new(new GalleryTypeRegistration { TypeId = type, ResourceTypeId = type }, []));
            var view = factory.Create(index);
            var root = new Border { Classes = { "galnet-player" }, Child = view }; root.Styles.Add(theme.CreateStyles());
            var window = new Window { Width = 1000, Height = 700, Content = root }; window.Show();
            try
            {
                window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var list = view.GetVisualDescendants().OfType<ItemsControl>().Single(control => control.Name == "TypeList");
                var buttons = list.GetVisualDescendants().OfType<Button>().ToArray();
                Assert.That(buttons, Has.Length.EqualTo(4));
                var first = buttons[0].TranslatePoint(default, root)!.Value;
                Assert.That(first.X + buttons[0].Bounds.Width / 2, Is.EqualTo(root.Bounds.Width / 2).Within(2));
                for (var i = 1; i < buttons.Length; i++)
                {
                    var current = buttons[i].TranslatePoint(default, root)!.Value;
                    var previous = buttons[i - 1].TranslatePoint(default, root)!.Value;
                    Assert.That(current.X, Is.EqualTo(first.X).Within(1));
                    Assert.That(current.Y, Is.GreaterThanOrEqualTo(previous.Y + buttons[i - 1].Bounds.Height));
                }
                Capture(window, $"{layout}-gallery-categories-centered");
                var video = scope.ServiceProvider.GetRequiredService<VideoGalleryPlayerPageViewModel>();
                root.Child = factory.Create(video); window.UpdateLayout();
                var controls = root.GetVisualDescendants().OfType<VideoGalleryControls>().Single();
                Assert.That(controls.FindControl<Slider>("Seek")!.IsEnabled, Is.False);
                Assert.That(controls.FindControl<Button>("PlayButton")!.IsEnabled, Is.False);
                video.HasVideo = true; video.IsPlaying = true; video.CanSeek = true;
                window.UpdateLayout();
                Assert.That(controls.FindControl<Button>("PauseButton")!.IsVisible, Is.True);
                Assert.That(controls.FindControl<Button>("PlayButton")!.IsVisible, Is.False);
                video.BeginSeek(); video.Progress = 55;
                Assert.That(video.Progress, Is.EqualTo(55)); video.EndSeek();
                Capture(window, $"{layout}-video-gallery-controls");
                window.Width = 480;
                await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Background);
                Assert.That(controls.FindControl<Slider>("Seek")!.Bounds.Width, Is.GreaterThan(20));
                Assert.That(Grid.GetRow(controls.FindControl<TextBlock>("TimeLabel")!), Is.EqualTo(1));
                Capture(window, $"{layout}-video-gallery-controls-narrow");
            }
            finally { window.Close(); }
        }, TestContext.CurrentContext.CancellationToken);
    }

    [TestCase("Classic", "cg")]
    [TestCase("Minimal", "cg")]
    [TestCase("Paper", "cg")]
    [TestCase("Classic", "video")]
    [TestCase("Minimal", "video")]
    [TestCase("Paper", "video")]
    [TestCase("Classic", "scene")]
    [TestCase("Minimal", "scene")]
    [TestCase("Paper", "scene")]
    public async Task LockedAggregationCardHasVisibleQuestionMarkCaptionOutsideDisabledPreview(string layout, string type)
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly);
        await session.Dispatch(async () =>
        {
            var theme = CreateTheme(layout, "Ocean");
            await using var provider = CreateServices(theme).BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            GalleryVisualPageViewModelBase page = type switch
            {
                "cg" => scope.ServiceProvider.GetRequiredService<CgGalleryPageViewModel>(),
                "video" => scope.ServiceProvider.GetRequiredService<VideoGalleryPageViewModel>(),
                _ => scope.ServiceProvider.GetRequiredService<SceneGalleryPageViewModel>()
            };
            page.Items.Add(new(new(new GalleryItem { Id = 2 }, false), "Spoiler"));
            var view = scope.ServiceProvider.GetRequiredService<IPageViewFactory>().Create(page);
            var root = new Border { Classes = { "galnet-player" }, Child = view }; root.Styles.Add(theme.CreateStyles());
            var window = new Window { Width = 1000, Height = 600, Content = root }; window.Show();
            try
            {
                window.UpdateLayout();
                var caption = view.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "CardTitle");
                Assert.That(caption.Text, Is.EqualTo("???"));
                Assert.That(caption.IsEffectivelyVisible && caption.IsEffectivelyEnabled, Is.True);
                Assert.That(caption.Bounds.Height, Is.GreaterThan(10));
                Assert.That(caption.GetVisualAncestors().OfType<Button>(), Is.Empty);
                Assert.That(view.GetVisualDescendants().OfType<Button>().Any(button => !button.IsEnabled), Is.True);
                Capture(window, $"{layout}-{type}-locked-caption");
            }
            finally { window.Close(); }
        }, TestContext.CurrentContext.CancellationToken);
    }

    [Test]
    public void AudioSeekDoesNotFeedBackendProgressBackIntoSeekingOrInterruptDragging()
    {
        var seeks = new List<double>();
        var item = new GalleryEntryViewModel(new(new GalleryItem { Id = 1 }, true), "Track", seeks.Add) { CanSeek = true };
        item.UpdateProgress(10); Assert.That(seeks, Is.Empty);
        item.BeginSeek(); item.Progress = 50; item.UpdateProgress(12); Assert.That(item.Progress, Is.EqualTo(50)); Assert.That(seeks, Is.Empty);
        item.EndSeek(); Assert.That(seeks, Is.EqualTo(new[] { 50d }));
        item.UpdateProgress(51); Assert.That(seeks, Has.Count.EqualTo(1));
    }
}
