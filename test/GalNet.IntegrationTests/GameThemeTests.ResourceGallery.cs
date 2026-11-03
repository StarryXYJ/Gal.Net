using VideoGalleryControls = GalNet.Sample.CrossPlatform.Views.VideoGalleryControls;
using AudioGalleryPageViewModel = GalNet.Sample.CrossPlatform.ViewModels.AudioGalleryPageViewModel;
using VideoGalleryPlayerPageViewModel = GalNet.Sample.CrossPlatform.ViewModels.VideoGalleryPlayerPageViewModel;
using SceneGalleryPageViewModel = GalNet.Sample.CrossPlatform.ViewModels.SceneGalleryPageViewModel;
using VideoGalleryPageViewModel = GalNet.Sample.CrossPlatform.ViewModels.VideoGalleryPageViewModel;
using CgGalleryPageViewModel = GalNet.Sample.CrossPlatform.ViewModels.CgGalleryPageViewModel;
using GalleryPageViewModel = GalNet.Sample.CrossPlatform.ViewModels.GalleryPageViewModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Avalonia.GameView.Controls;
using GalNet.Contracts.Builtins.Gallery;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Presentation;

public sealed partial class GameThemeTests
{
    private sealed class ControlledGalleryPage(GalNet.Avalonia.GameView.Services.GalleryMediaService media)
        : GalNet.Sample.CrossPlatform.ViewModels.GalleryGridPageViewModel(null!, media)
    {
        public List<TaskCompletionSource<global::Avalonia.Media.Imaging.Bitmap?>> Loads { get; } = [];
        protected override Task<global::Avalonia.Media.Imaging.Bitmap?> LoadThumbnailAsync(GalleryItemData item, CancellationToken cancellationToken)
        {
            var result = new TaskCompletionSource<global::Avalonia.Media.Imaging.Bitmap?>();
            Loads.Add(result);
            return result.Task;
        }
        protected override Task OpenDetailAsync(GalleryItemData item, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Test]
    public async Task GalleryPublishesImmediatelyAndDrainsCancelledLoadsOnDisposal()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(GeneralTest.Integration.ScreenshotTestApplication).Assembly);
        await session.Dispatch(async () =>
        {
            using var media = new GalNet.Avalonia.GameView.Services.GalleryMediaService(null!);
            var page = new ControlledGalleryPage(media);
            var data = new GalleryTypeData(new GalleryTypeRegistration { TypeId = "cg", ResourceTypeId = "image" },
                Enumerable.Range(0, 25).Select(index => new GalleryItemData(new GalleryItem { Id = index }, true)).ToArray());
            await page.ActivateAsync(data);
            Assert.That(page.Pages.Items, Has.Count.EqualTo(12));
            Assert.That(page.Pages.Items[0].IsThumbnailLoading, Is.True);
            page.Pages.CurrentPage = 2;
            page.Pages.CurrentPage = 1;
            Assert.That(page.Loads, Has.Count.EqualTo(3));
            page.Loads[0].SetResult(CreateGalleryArtwork());
            await Task.Yield();
            Assert.That(page.Pages.Items[0].Thumbnail, Is.Null);
            Assert.That(page.Pages.Items[0].IsThumbnailLoading, Is.True);
            var disposal = page.DisposeAsync().AsTask();
            Assert.That(disposal.IsCompleted, Is.False);
            page.Loads[1].SetResult(null);
            page.Loads[2].SetResult(null);
            await disposal;
            Assert.That(page.Thumbnails.Items, Is.Empty);
        }, TestContext.CurrentContext.CancellationToken);
    }

    [Test]
    public async Task GalleryAndAudioPaginationKeepOnlyTwelveRenderedRowsAndRetainPlaybackState()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(GeneralTest.Integration.ScreenshotTestApplication).Assembly);
        await session.Dispatch(async () =>
        {
            await using var provider = CreateServices(new TemplateAppearance()).BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var gallery = services.GetRequiredService<CgGalleryPageViewModel>();
            var audio = services.GetRequiredService<AudioGalleryPageViewModel>();
            for (var index = 0; index < 25; index++)
            {
                gallery.Thumbnails.Items.Add(new(new(new GalleryItem { Id = index }, true), $"Image {index}"));
                audio.State.Items.Add(new(new(new GalleryItem { Id = index }, true), $"Audio {index}"));
            }
            gallery.Thumbnails.Items[0].IsThumbnailLoading = true;
            audio.State.Items[0].IsPlaying = true;
            var semi = new Semi.Avalonia.SemiTheme();
            var ursa = new Ursa.Themes.Semi.UrsaSemiTheme();
            Application.Current!.Styles.Add(semi); Application.Current.Styles.Add(ursa);
            var root = new Border { Classes = { "galnet-player" } };
            root.Styles.Add(GalNet.Sample.CrossPlatform.SampleCustomization.CreateStyles());
            var window = new Window { Width = 1280, Height = 720, Content = root };
            window.Show();
            try
            {
                root.Child = services.GetRequiredService<IPageViewFactory>().Create(gallery);
                window.UpdateLayout();
                Assert.That(root.GetVisualDescendants().OfType<Border>().Count(x => x.Name == "GalleryCard"), Is.EqualTo(12));
                var skeleton = root.GetVisualDescendants().OfType<Ursa.Controls.Skeleton>().Single(x => x.IsVisible);
                Assert.That(skeleton.Bounds.Width, Is.GreaterThan(0));
                Assert.That(skeleton.GetVisualDescendants().Any(), Is.True, "The Ursa loading template must be applied.");
                var pager = root.GetVisualDescendants().OfType<Ursa.Controls.Pagination>().Single();
                Click(window, pager.GetVisualDescendants().OfType<Ursa.Controls.PaginationButton>().Single(x => x.Name == "PART_NextButton"));
                window.UpdateLayout();
                Assert.That(gallery.Pages.CurrentPage, Is.EqualTo(2));
                Assert.That(gallery.Pages.Items.Select(x => x.Id), Is.EqualTo(Enumerable.Range(12, 12)));
                root.Child = services.GetRequiredService<IPageViewFactory>().Create(audio);
                window.UpdateLayout();
                Assert.That(root.GetVisualDescendants().OfType<AudioGalleryRow>().Count(), Is.EqualTo(12));
                pager = root.GetVisualDescendants().OfType<Ursa.Controls.Pagination>().Single();
                Click(window, pager.GetVisualDescendants().OfType<Ursa.Controls.PaginationButton>().Single(x => x.Name == "PART_NextButton"));
                window.UpdateLayout();
                Assert.That(audio.Pages.CurrentPage, Is.EqualTo(2));
                Assert.That(audio.State.Items[0].IsPlaying, Is.True);
                Assert.That(audio.Pages.Items.Select(x => x.Id), Is.EqualTo(Enumerable.Range(12, 12)));
            }
            finally { window.Close(); Application.Current.Styles.Remove(ursa); Application.Current.Styles.Remove(semi); }
        }, TestContext.CurrentContext.CancellationToken);
    }

    [TestCase(360)]
    [TestCase(1000)]
    [TestCase(1920)]
    public async Task VisualGalleryCardsMatchSaveCardsAndKeepTheirPreviewAndClickTargets(int width)
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(GeneralTest.Integration.ScreenshotTestApplication).Assembly);
        await session.Dispatch(async () =>
        {
            var theme = CreateTheme("Classic", "Ocean");
            await using var provider = CreateServices(theme).BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var factory = services.GetRequiredService<IPageViewFactory>();
            var saves = services.GetRequiredService<GalNet.Sample.CrossPlatform.ViewModels.SaveSlotsPageViewModel>();
            saves.Slots.Clear();
            for (var index = 0; index < 8; index++)
                saves.Slots.Add(new(new(index, default, "Save", true, false), false, _ => Task.CompletedTask, _ => Task.CompletedTask));
            var root = new Border { Classes = { "galnet-player" }, Child = factory.Create(saves) };
            root.Styles.Add(theme.CreateStyles());
            var window = new Window { Width = width, Height = 800, Content = root };
            Rect Bounds(Control control) => new(control.TranslatePoint(default, root)!.Value, control.Bounds.Size);
            window.Show();
            try
            {
                window.UpdateLayout();
                var expected = root.GetVisualDescendants().OfType<GalNet.Game.Controls.SaveSlotCard>()
                    .Where(card => card.IsVisible).Select(Bounds).ToArray();
                Assert.That(expected, Has.Length.EqualTo(8));
                Capture(window, $"cards-save-{width}");
                GalNet.Sample.CrossPlatform.ViewModels.GalleryGridPageViewModel[] pages =
                    [services.GetRequiredService<CgGalleryPageViewModel>(), services.GetRequiredService<VideoGalleryPageViewModel>(), services.GetRequiredService<SceneGalleryPageViewModel>()];
                foreach (var page in pages)
                {
                    for (var index = 0; index < 8; index++)
                        page.Thumbnails.Items.Add(new(new(new GalleryItem { Id = index }, index != 1), $"Memory {index}")
                            { Thumbnail = index == 0 ? CreateGalleryArtwork() : null });
                    root.Child = factory.Create(page);
                    window.UpdateLayout();
                    var cards = root.GetVisualDescendants().OfType<Border>().Where(card => card.Name == "GalleryCard").ToArray();
                    Assert.That(cards.Select(Bounds), Is.EqualTo(expected), "Card size, columns, spacing and page inset must all match saves.");
                    var first = cards[0];
                    var preview = first.GetVisualDescendants().OfType<Image>().Single();
                    Assert.That(preview.Source, Is.Not.Null);
                    Assert.That(preview.Bounds.Height, Is.EqualTo(first.Bounds.Height - 2));
                    var button = first.GetVisualDescendants().OfType<Button>().Single();
                    Assert.That(button.Command, Is.SameAs(page.OpenItemCommand));
                    Assert.That(button.CommandParameter, Is.SameAs(page.Thumbnails.Items[0]));
                    var activations = 0;
                    button.Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => activations++);
                    var caption = first.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "CardTitle");
                    foreach (var target in new Control[] { preview, caption })
                    {
                        var point = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
                        window.MouseDown(point, global::Avalonia.Input.MouseButton.Left);
                        window.MouseUp(point, global::Avalonia.Input.MouseButton.Left);
                    }
                    Assert.That(activations, Is.EqualTo(2), "Both the picture and caption open the card.");
                    Capture(window, $"cards-{page.GetType().Name}-{width}");
                }
            }
            finally { window.Close(); }
        }, TestContext.CurrentContext.CancellationToken);
    }

    [TestCase("Classic")]


    public async Task AudioGalleryRowsKeepNamesTooltipsAndControlsReadableAtWideAndNarrowSizes(string layout)
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(GeneralTest.Integration.ScreenshotTestApplication).Assembly);
        await session.Dispatch(async () =>
        {
            var theme = CreateTheme(layout, "Mono");
            await using var provider = CreateServices(theme).BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var page = scope.ServiceProvider.GetRequiredService<AudioGalleryPageViewModel>();
            const string name = "A long music title / 很长的曲名，用来检查省略显示和完整提示，而不会挤压播放按钮";
            var entry = new GalleryEntryViewModel(new(new GalleryItem { Id = 1 }, true), name);
            page.State.Items.Add(entry); page.State.Items.Add(new(new(new GalleryItem { Id = 2 }, false), "Hidden spoiler"));
            var view = scope.ServiceProvider.GetRequiredService<IPageViewFactory>().Create(page);
            var root = new Border { Classes = { "galnet-player" }, Child = view }; root.Styles.Add(theme.CreateStyles());
            var window = new Window { Width = 1000, Height = 600, Content = root }; window.Show();
            try
            {
                window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var rows = view.GetVisualDescendants().OfType<AudioGalleryRow>().ToArray();
                Assert.That(rows, Has.Length.EqualTo(2));
                var title = rows[0].FindControl<TextBlock>("TrackName")!;
                Assert.That(title.TextAlignment, Is.EqualTo(TextAlignment.Left));
                Assert.That(title.Text, Is.EqualTo(name)); Assert.That(title.TextTrimming, Is.EqualTo(TextTrimming.CharacterEllipsis));
                Assert.That(ToolTip.GetTip(title), Is.EqualTo(name));
                Assert.That(rows[0].ToggleCommand, Is.SameAs(page.State.ToggleAudioCommand));
                entry.TimeText = "01:23 / 04:56";
                window.UpdateLayout();
                var time = rows[0].FindControl<TextBlock>("TrackTime")!;
                Assert.That(time.Text, Is.EqualTo("01:23 / 04:56"));
                Assert.That(time.FontSize, Is.LessThanOrEqualTo(12));
                Assert.That(time.HorizontalAlignment, Is.EqualTo(global::Avalonia.Layout.HorizontalAlignment.Right));
                Assert.That(Grid.GetRow(rows[0].FindControl<Grid>("Timeline")!), Is.Zero);
                Assert.That(((GalleryEntryViewModel)rows[1].DataContext!).Name, Is.Empty);
                var lockedTitle = rows[1].FindControl<TextBlock>("TrackName")!;
                Assert.That(lockedTitle.Text, Is.EqualTo("???"));
                Assert.That(lockedTitle.IsEffectivelyEnabled, Is.True, "Only playback controls should be disabled; the locked title must remain readable.");
                Assert.That(ToolTip.GetTip(lockedTitle), Is.Null);
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


    public async Task SceneAndImageGalleryHaveIndependentViewsAndLockedNamesStayHidden(string layout)
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(GeneralTest.Integration.ScreenshotTestApplication).Assembly);
        await session.Dispatch(async () =>
        {
            var theme = CreateTheme(layout, "Ocean");
            await using var provider = CreateServices(theme).BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var factory = scope.ServiceProvider.GetRequiredService<IPageViewFactory>();
            var images = scope.ServiceProvider.GetRequiredService<CgGalleryPageViewModel>();
            var scenes = scope.ServiceProvider.GetRequiredService<SceneGalleryPageViewModel>();
            scenes.Thumbnails.Items.Add(new(new(new GalleryItem { Id = 400 }, true), "A scene memory / 场景回忆"));
            scenes.Thumbnails.Items.Add(new(new(new GalleryItem { Id = 401 }, false), "Spoiler"));
            var imageView = factory.Create(images); var sceneView = factory.Create(scenes);
            Assert.That(sceneView, Is.Not.SameAs(imageView)); Assert.That(imageView.DataContext, Is.SameAs(images));
            Assert.That(scope.ServiceProvider.GetRequiredService<IGalleryPageRegistry>().TryGetViewModelType("scene", out var type), Is.True);
            Assert.That(type, Is.EqualTo(typeof(SceneGalleryPageViewModel)));
            var root = new Border { Classes = { "galnet-player" }, Child = sceneView }; root.Styles.Add(theme.CreateStyles());
            var window = new Window { Width = 1000, Height = 600, Content = root }; window.Show();
            try { window.UpdateLayout(); Assert.That(scenes.Thumbnails.Items[1].HasName, Is.False); Assert.That(scenes.Thumbnails.Items[1].Title, Is.EqualTo("???")); Capture(window, $"{layout}-scene-gallery"); }
            finally { window.Close(); }
        }, TestContext.CurrentContext.CancellationToken);
    }
    [TestCase("Classic")]


    public async Task GalleryCategoriesAreCenteredInOneColumnAndVideoControlsFollowPlaybackState(string layout)
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(GeneralTest.Integration.ScreenshotTestApplication).Assembly);
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
                video.State.HasVideo = true; video.State.IsPlaying = true; video.State.CanSeek = true;
                window.UpdateLayout();
                Assert.That(controls.FindControl<Button>("PauseButton")!.IsVisible, Is.True);
                Assert.That(controls.FindControl<Button>("PlayButton")!.IsVisible, Is.False);
                video.State.BeginSeek(); video.State.Progress = 55;
                Assert.That(video.State.Progress, Is.EqualTo(55)); video.State.EndSeek();
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


    [TestCase("Classic", "video")]


    [TestCase("Classic", "scene")]


    public async Task LockedAggregationCardHasVisibleQuestionMarkCaptionOutsideDisabledPreview(string layout, string type)
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(GeneralTest.Integration.ScreenshotTestApplication).Assembly);
        await session.Dispatch(async () =>
        {
            var theme = CreateTheme(layout, "Ocean");
            await using var provider = CreateServices(theme).BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            GalNet.Sample.CrossPlatform.ViewModels.GalleryGridPageViewModel page = type switch
            {
                "cg" => scope.ServiceProvider.GetRequiredService<CgGalleryPageViewModel>(),
                "video" => scope.ServiceProvider.GetRequiredService<VideoGalleryPageViewModel>(),
                _ => scope.ServiceProvider.GetRequiredService<SceneGalleryPageViewModel>()
            };
            page.Thumbnails.Items.Add(new(new(new GalleryItem { Id = 2 }, false), "Spoiler"));
            page.Thumbnails.Items.Add(new(new(new GalleryItem { Id = 3 }, true)));
            page.Thumbnails.Items.Add(new(new(new GalleryItem { Id = 4 }, true), "Named resource"));
            var view = scope.ServiceProvider.GetRequiredService<IPageViewFactory>().Create(page);
            var root = new Border { Classes = { "galnet-player" }, Child = view }; root.Styles.Add(theme.CreateStyles());
            var window = new Window { Width = 1000, Height = 600, Content = root }; window.Show();
            try
            {
                window.UpdateLayout();
                var captions = view.GetVisualDescendants().OfType<TextBlock>().Where(text => text.Name == "CardTitle").ToArray();
                Assert.That(captions, Has.Length.EqualTo(3));
                Assert.That(captions.All(text => text.TextAlignment == TextAlignment.Center), Is.True);
                var caption = captions.Single(text => ((GalleryEntryViewModel)text.DataContext!).IsLocked);
                var unnamedButton = view.GetVisualDescendants().OfType<Button>().Single(button => button.DataContext is GalleryEntryViewModel { Id: 3 });
                Assert.That(unnamedButton.IsEffectivelyEnabled, Is.True, "An unlocked card without a name must still open.");
                Assert.That(ToolTip.GetTip(unnamedButton), Is.Null);
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
