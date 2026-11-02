using MissingGalleryPageViewModel = GalNet.Sample.CrossPlatform.ViewModels.MissingGalleryPageViewModel;
using SceneGalleryPlayerPageViewModel = GalNet.Sample.CrossPlatform.ViewModels.SceneGalleryPlayerPageViewModel;
using VideoGalleryPlayerPageViewModel = GalNet.Sample.CrossPlatform.ViewModels.VideoGalleryPlayerPageViewModel;
using CgGalleryImagePageViewModel = GalNet.Sample.CrossPlatform.ViewModels.CgGalleryImagePageViewModel;
using GalleryPageViewModel = GalNet.Sample.CrossPlatform.ViewModels.GalleryPageViewModel;
using AboutPageViewModel = GalNet.Sample.CrossPlatform.ViewModels.AboutPageViewModel;
using SettingsPageViewModel = GalNet.Sample.CrossPlatform.ViewModels.SettingsPageViewModel;
using SaveSlotsPageViewModel = GalNet.Sample.CrossPlatform.ViewModels.SaveSlotsPageViewModel;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Ursa.Controls;
using Avalonia.VisualTree;
using Avalonia.Threading;
using GalNet.Avalonia.GameHost;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.Composition;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.Sample.CrossPlatform.ViewModels;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Presentation.Abstractions.Application;
using GalNet.Sample.CrossPlatform;
using Microsoft.Extensions.DependencyInjection;
using SampleGame = GalNet.Sample.CrossPlatform.ViewModels.GamePageViewModel;
using SampleTitle = GalNet.Sample.CrossPlatform.ViewModels.TitlePageViewModel;
using SampleGameView = GalNet.Sample.CrossPlatform.Views.GamePage;

namespace GalNet.IntegrationTests;

[NonParallelizable]
public sealed class SampleTemplatePagesTests
{
    [Test]
    public Task RuntimeSurfaceStillUsesTheProjectsScopedPageFactory() => SampleUiTests.RunAsync(async () =>
    {
        var collection = new ServiceCollection();
        collection.AddSingleton<IGameSessionService>(new Session());
        SampleCustomization.ConfigureServices(collection);
        collection.AddScoped<IPageViewFactory, DecoratingPageFactory>();
        await using var provider = collection.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var page = scope.ServiceProvider.GetRequiredService<GamePage>();
        Assert.That(page.Tag, Is.EqualTo("project-factory"));
        Assert.That(page, Is.SameAs(scope.ServiceProvider.GetRequiredService<IPageViewFactory>()
            .Create(scope.ServiceProvider.GetRequiredService<SampleGame>())));
    });

    [Test]
    public Task RegisteredGameLayoutIsUsedForBothPlayerAndIndependentReplay() => SampleUiTests.RunAsync(async () =>
    {
        var session = new Session();
        var collection = new ServiceCollection();
        collection.AddSingleton<IGameSessionService>(session);
        SampleCustomization.ConfigureServices(collection, views => views.Register<SampleGame, ReplacementGameView>());
        await using var provider = collection.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var player = (ReplacementGameView)services.GetRequiredService<GamePage>();
        var replay = services.GetRequiredService<GalleryScenePlayback>();
        Assert.That(replay.Surface, Is.TypeOf<ReplacementGameView>());
        var preview = (ReplacementGameView)replay.Surface;
        Assert.That(player, Is.SameAs(services.GetRequiredService<IPageViewFactory>().Create(services.GetRequiredService<SampleGame>())));
        Assert.That(preview, Is.Not.SameAs(player));
        Assert.That(player.InjectedState, Is.SameAs(services.GetRequiredService<GamePresentationState>()));
        Assert.That(preview.InjectedState, Is.SameAs(replay.Gameplay).And.Not.SameAs(player.InjectedState));
        Assert.That(preview.InjectedModel, Is.SameAs(preview.DataContext).And.Not.SameAs(player.DataContext));
        Assert.That(preview.InjectedModel.State, Is.SameAs(replay.Gameplay));
        Assert.That(preview.InjectedNavigation, Is.SameAs(player.InjectedNavigation));
    });

    [Test]
    public Task BareGameControlMappingDoesNotRecurseThroughItsScopedAlias() => SampleUiTests.RunAsync(async () =>
    {
        var collection = new ServiceCollection();
        collection.AddSingleton<IGameSessionService>(new Session());
        SampleCustomization.ConfigureServices(collection, views => views.Register<SampleGame, GamePage>());
        await using var provider = collection.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var page = services.GetRequiredService<GamePage>();
        Assert.That(page, Is.SameAs(services.GetRequiredService<IPageViewFactory>().Create(services.GetRequiredService<SampleGame>())));
        Assert.That(page.Presentation, Is.SameAs(services.GetRequiredService<GamePresentationState>()));
    });

    [Test]
    public Task ProjectOwnsAnnouncementDurationAndRejectsInvalidLifetimes() => SampleUiTests.RunAsync(() =>
    {
        var view = SampleCustomization.CreateView();
        Assert.That(view.AudioAnnouncementDuration, Is.EqualTo(TimeSpan.FromSeconds(2.8)));
        Assert.Throws<ArgumentException>(() => view.AudioAnnouncementDuration = TimeSpan.Zero);
        Assert.Throws<ArgumentException>(() => view.AudioAnnouncementDuration = TimeSpan.FromSeconds(-1));
        return Task.CompletedTask;
    });

    [Test]
    public Task CustomAnnouncementLifetimeClosesTheActualCard() => SampleUiTests.RunAsync(async () =>
    {
        await using var host = await AvaloniaPlayerHost.CreateAsync(SampleCustomization.CreateHostOptions(),
            services => SampleCustomization.ConfigureServices(services));
        var semi = new Semi.Avalonia.SemiTheme();
        var ursa = new Ursa.Themes.Semi.UrsaSemiTheme();
        Application.Current!.Styles.Add(semi);
        Application.Current.Styles.Add(ursa);
        var window = new Window { Width = 800, Height = 450, Content = host.View };
        try
        {
            var surface = (GalNet.Avalonia.GameHost.Views.PlayerSurface)host.View;
            surface.AudioAnnouncementDuration = TimeSpan.FromMilliseconds(400);
            surface.TextCards.EnterDuration = TimeSpan.Zero;
            surface.TextCards.ExitDuration = TimeSpan.Zero;
            window.Show();
            host.Services.GetRequiredService<GamePresentationState>().AnnounceAudio("Short project announcement");
            await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Background);
            Assert.That(surface.TextCards.Children.OfType<ContentControl>().Single().Content, Is.EqualTo("Short project announcement"));
            await Task.Delay(700);
            Assert.That(surface.TextCards.Children, Is.Empty, "The configured lifetime must be used instead of the generic hold duration.");
        }
        finally { window.Close(); Application.Current.Styles.Remove(ursa); Application.Current.Styles.Remove(semi); }
    });

    [Test]
    public Task ProjectPagesOwnTheirBindingsAndBeginOnlyAfterGameIsMounted() => SampleUiTests.RunAsync(async () =>
    {
        var session = new Session();
        await using var provider = Services(session).BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var navigation = services.GetRequiredService<IGameNavigationService>();
        navigation.ResetTo<SampleTitle>(NavigationTransition.None);
        var window = new Window { Width = 1280, Height = 720, Content = services.GetRequiredService<GameShell>() };
        window.Show();
        try
        {
            var title = services.GetRequiredService<SampleTitle>();
            var button = window.GetVisualDescendants().OfType<Button>().Single(control => control.Name == "NewGameButton");
            Assert.That(button.Command, Is.SameAs(title.StartNewGameCommand));
            Assert.That(title.GetType().BaseType, Is.EqualTo(typeof(PageViewModelBase)), "No built-in title VM inheritance is needed.");
            session.BeginCheck = () =>
            {
                Assert.That(navigation.CurrentViewModel, Is.TypeOf<SampleGame>());
                Assert.That(window.GetVisualDescendants(), Does.Contain(services.GetRequiredService<SampleGameView>()));
            };
            await services.GetRequiredService<IPlayerApplication>().StartNewGameAsync();
            var game = services.GetRequiredService<SampleGame>();
            Assert.That(game.State, Is.SameAs(services.GetRequiredService<GamePresentationState>()));
            Assert.That(services.GetRequiredService<GalNet.Avalonia.GameView.Page.GamePage>().Presentation, Is.SameAs(game.State));
            Assert.That(session.Begins, Is.EqualTo(1));

            // A new page requires only its model, view and explicit project registration.
            await navigation.NavigateAsync(services.GetRequiredService<Notebook>(), NavigationTransition.None);
            window.UpdateLayout();
            Assert.That(window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "Notebook"), Is.True);
            navigation.GoBack(NavigationTransition.None);
            Assert.That(navigation.CurrentViewModel, Is.SameAs(game));
            navigation.ResetTo<SampleTitle>(NavigationTransition.None);
            await title.ContinueCommand.ExecuteAsync(null);
            Assert.That(session.Begins, Is.EqualTo(2));
            Assert.That(navigation.CurrentViewModel, Is.SameAs(game));
        }
        finally { window.Close(); }
    });

    [TestCase(false)]
    [TestCase(true)]
    public Task ProjectLaunchReturnsToItsOwnTitleAfterFailureOrCancellation(bool cancel) => SampleUiTests.RunAsync(async () =>
    {
        var preparing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new Session
        {
            Prepare = async token =>
            {
                preparing.TrySetResult();
                if (cancel) await Task.Delay(Timeout.Infinite, token);
                else throw new IOException("Project preparation failed.");
            }
        };
        await using var provider = Services(session).BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var navigation = scope.ServiceProvider.GetRequiredService<IGameNavigationService>();
        navigation.ResetTo<SampleTitle>(NavigationTransition.None);
        var window = new Window { Content = scope.ServiceProvider.GetRequiredService<GameShell>() };
        window.Show();
        try
        {
            var title = scope.ServiceProvider.GetRequiredService<SampleTitle>();
            var launching = title.StartNewGameCommand.ExecuteAsync(null);
            await preparing.Task;
            if (cancel) title.StartNewGameCommand.Cancel();
            try { await launching; Assert.Fail("The failure/cancellation must propagate."); }
            catch (OperationCanceledException) when (cancel) { }
            catch (IOException) when (!cancel) { }
            Assert.That(navigation.CurrentViewModel, Is.SameAs(title));
            Assert.That(title.IsLoading, Is.False);
            Assert.That(session.Begins, Is.Zero);
        }
        finally { window.Close(); }
    });

    [Test]
    public Task ProjectToolbarNavigatesWithoutAdvancingTheStory() => SampleUiTests.RunAsync(async () =>
    {
        await using var provider = Services(new Session()).BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var navigation = services.GetRequiredService<IGameNavigationService>();
        var state = services.GetRequiredService<GamePresentationState>();
        state.IsGameUiReady = true;
        var advances = 0;
        state.AdvanceRequested += () => advances++;
        navigation.ResetTo<SampleGame>(NavigationTransition.None);
        var window = new Window { Width = 1280, Height = 720, Content = services.GetRequiredService<GameShell>() };
        window.Show();
        try
        {
            window.UpdateLayout();
            var button = window.GetVisualDescendants().OfType<Button>().Single(control => control.Name == "SettingsButton");
            Assert.That(button.Command, Is.SameAs(services.GetRequiredService<SampleGame>().OpenSettingsCommand));
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Assert.That(navigation.CurrentViewModel, Is.TypeOf<GalNet.Sample.CrossPlatform.ViewModels.SettingsPageViewModel>());
            Assert.That(advances, Is.Zero);
        }
        finally { window.Close(); }
    });

    [Test]
    public Task AllApplicationPagesUseProjectViewsAndExactGalleryRoutes() => SampleUiTests.RunAsync(async () =>
    {
        await using var provider = Services(new Session()).BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var navigation = services.GetRequiredService<IGameNavigationService>();
        var application = services.GetRequiredService<IPlayerApplication>();
        application.Attach(); application.CompleteStartup(null); application.ShowTitle();
        var window = new Window { Width = 1280, Height = 720, Content = application.View };
        window.Show();
        try
        {
            var factory = services.GetRequiredService<IPageViewFactory>();
            var ordinary = new PageViewModelBase[]
            {
                services.GetRequiredService<SettingsPageViewModel>(), services.GetRequiredService<SaveSlotsPageViewModel>(),
                services.GetRequiredService<AboutPageViewModel>(), services.GetRequiredService<GalleryPageViewModel>(),
                services.GetRequiredService<CgGalleryImagePageViewModel>(), services.GetRequiredService<VideoGalleryPlayerPageViewModel>(),
                services.GetRequiredService<SceneGalleryPlayerPageViewModel>()
            };
            var capture = Environment.GetEnvironmentVariable("GALNET_THEME_CAPTURE_DIR");
            foreach (var page in ordinary)
            {
                await navigation.NavigateAsync(page, NavigationTransition.None);
                window.UpdateLayout();
                var view = factory.Create(page);
                Assert.That(view.GetType().Namespace, Is.EqualTo("GalNet.Sample.CrossPlatform.Views"));
                if (view.FindControl<Button>("PageBackButton") is { } back)
                    Assert.That(back.Command, Is.SameAs(((BackNavigablePageViewModel)page).BackCommand));
                if (capture is not null)
                {
                    Directory.CreateDirectory(capture); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var bitmap = window.CaptureRenderedFrame();
                    bitmap!.Save(Path.Combine(capture, "project-" + page.GetType().Name + ".png"));
                }
                navigation.GoBack(NavigationTransition.None);
            }
            var galleryNavigation = services.GetRequiredService<GalleryNavigationService>();
            foreach (var type in new[] { "cg", "audio", "video", "scene" })
            {
                var gallery = new GalleryTypeData(new GalleryTypeRegistration { TypeId = type, ResourceTypeId = type },
                    [new(new GalleryItem { Id = 42 }, false)]);
                await galleryNavigation.OpenTypeAsync(gallery);
                Assert.That(navigation.CurrentViewModel!.GetType().Namespace, Is.EqualTo("GalNet.Sample.CrossPlatform.ViewModels"));
                Assert.That(factory.Create(navigation.CurrentViewModel).GetType().Namespace, Is.EqualTo("GalNet.Sample.CrossPlatform.Views"));
                ((BackNavigablePageViewModel)navigation.CurrentViewModel).BackCommand.Execute(null);
            }
            await galleryNavigation.OpenTypeAsync(new(new GalleryTypeRegistration { TypeId = "custom", ResourceTypeId = "cg" }, []));
            Assert.That(navigation.CurrentViewModel, Is.TypeOf<MissingGalleryPageViewModel>());
            Assert.That(((MissingGalleryPageViewModel)navigation.CurrentViewModel!).Message, Does.Contain("custom"));
            Assert.That(services.GetRequiredService<IPageViewRegistry>().GetViewType(typeof(ScreenshotDialogViewModel)),
                Is.EqualTo(typeof(GalNet.Sample.CrossPlatform.Views.ScreenshotDialog)));
            var replay = services.GetRequiredService<GalleryScenePlayback>();
            Assert.That(replay.Surface, Is.TypeOf<SampleGameView>());
            Assert.That(replay.Gameplay, Is.Not.SameAs(services.GetRequiredService<GamePresentationState>()));
        }
        finally { window.Close(); application.Detach(); }
    });

    [Test]
    public Task OptionalPagesAreNotRequiredAndMissingRoutesIdentifyThePage() => SampleUiTests.RunAsync(() =>
    {
        var services = new ServiceCollection();
        services.AddAvaloniaGameView(views => views.Register<Notebook, NotebookView>());
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var navigation = scope.ServiceProvider.GetRequiredService<IGameNavigationService>();
        navigation.ResetTo<Notebook>(NavigationTransition.None);
        Assert.That(scope.ServiceProvider.GetRequiredService<IPageViewFactory>().Create(navigation.CurrentViewModel!), Is.TypeOf<NotebookView>());
        Assert.That(() => scope.ServiceProvider.GetRequiredService<IPageViewRegistry>().GetViewType(typeof(AboutPageViewModel)),
            Throws.InvalidOperationException.With.Message.Contains(nameof(AboutPageViewModel)));
        return Task.CompletedTask;
    });

    [Test]
    public Task ProjectSavePageUsesRowCommandsAndPreservesRowsAcrossRename() => SampleUiTests.RunAsync(async () =>
    {
        var session = new Session();
        await using var provider = Services(session).BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var navigation = services.GetRequiredService<IGameNavigationService>();
        navigation.ResetTo<SampleGame>(NavigationTransition.None);
        var window = new Window { Content = services.GetRequiredService<GameShell>() };
        window.Show();
        try
        {
            await services.GetRequiredService<SampleGame>().OpenSaveSlotsCommand.ExecuteAsync(null);
            var saves = (SaveSlotsPageViewModel)navigation.CurrentViewModel!;
            Assert.That(saves.IsSaveMode, Is.True);
            await saves.Slots[0].SaveCommand.ExecuteAsync(null);
            Assert.That(session.Saves, Is.EqualTo(1));
            Assert.That(navigation.CurrentViewModel, Is.TypeOf<SampleGame>());
            await services.GetRequiredService<SampleGame>().OpenLoadSlotsCommand.ExecuteAsync(null);
            var row = saves.Slots[0];
            await row.RenameCommand.ExecuteAsync("A project memory");
            Assert.That(saves.Slots[0], Is.SameAs(row));
            Assert.That(row.Description, Is.EqualTo("A project memory"));
            window.UpdateLayout();
            var card = window.GetVisualDescendants().OfType<GalNet.Game.Controls.SaveSlotCard>().Single(control => control.IsVisible);
            Assert.That(card.Command, Is.SameAs(row.LoadCommand));
            await row.LoadCommand.ExecuteAsync(null);
            Assert.That(session.Begins, Is.EqualTo(1));
            Assert.That(navigation.CurrentViewModel, Is.TypeOf<SampleGame>());
        }
        finally { window.Close(); }
    });

    [Test]
    public Task ProjectScreenshotLayoutPreviewsCancelsAndExportsFullPixels() => SampleUiTests.RunAsync(async () =>
    {
        var destination = new ScreenshotDestination();
        var registrations = Services(new Session());
        registrations.AddSingleton<IAutomaticGameScreenshotDestination>(destination);
        await using var provider = registrations.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var overlay = new OverlayDialogHost { IsAnimationDisabled = true };
        var window = new Window { Width = 1000, Height = 850, Content = overlay };
        using var preview = new RenderTargetBitmap(new PixelSize(16, 9));
        using var stream = new MemoryStream(); preview.Save(stream);
        var bytes = stream.ToArray();
        window.Show();
        try
        {
            var previews = 0; var exports = 0;
            var request = new GameScreenshotRequest("Project", window, _ =>
            {
                exports++; return Task.FromResult<byte[]>([1, 2, 3, 4]);
            })
            { CapturePreviewPngAsync = _ => { previews++; return Task.FromResult(bytes); } };
            var service = scope.ServiceProvider.GetRequiredService<IGameScreenshotService>();
            var capturing = service.CaptureAsync(request);
            var dialog = (GalNet.Sample.CrossPlatform.Views.ScreenshotDialog)overlay.Children.OfType<CustomDialogControl>().Single().Content!;
            Assert.That(dialog.FindControl<Image>("Preview")!.Source, Is.Not.Null);
            dialog.FindControl<Button>("CancelButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await capturing.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(exports, Is.Zero);
            capturing = service.CaptureAsync(request);
            dialog = (GalNet.Sample.CrossPlatform.Views.ScreenshotDialog)overlay.Children.OfType<CustomDialogControl>().Single().Content!;
            dialog.FindControl<CheckBox>("IncludeUiBox")!.IsChecked = true;
            dialog.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await capturing.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(exports, Is.EqualTo(1));
            Assert.That(previews, Is.EqualTo(3));
            Assert.That(destination.Pixels, Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
            Assert.That(dialog.FindControl<Image>("Preview")!.Source, Is.Null);
            Assert.That(overlay.Children, Is.Empty);
        }
        finally { window.Close(); }
    });

    private sealed class ScreenshotDestination : IAutomaticGameScreenshotDestination
    {
        public byte[]? Pixels { get; private set; }
        public Task SaveAsync(string gameTitle, string fileName, ReadOnlyMemory<byte> png, CancellationToken cancellationToken = default)
        { Pixels = png.ToArray(); return Task.CompletedTask; }
    }

    [Test]
    public void EnabledMechanismsRejectIncompatibleViewsAtComposition()
    {
        Assert.That(() => new ServiceCollection().AddAvaloniaGameView(views => views.Register<SampleGame, NotebookView>()),
            Throws.InvalidOperationException.With.Message.Contains(typeof(SampleGame).FullName!).And.Message.Contains("GamePage-derived"));
        Assert.That(() => new ServiceCollection().AddAvaloniaGameView(views => views.Register<ScreenshotDialogViewModel, NotebookView>()),
            Throws.InvalidOperationException.With.Message.Contains(nameof(ScreenshotDialogViewModel)));
    }

    private static ServiceCollection Services(Session session)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IGameSessionService>(session);
        services.AddScoped<Notebook>();
        SampleCustomization.ConfigureServices(services, views => views.Register<Notebook, NotebookView>());
        return services;
    }

    public sealed class Notebook : PageViewModelBase;
    private sealed class DecoratingPageFactory(IServiceProvider services, IPageViewRegistry registry) : IPageViewFactory
    {
        private readonly PageViewFactory _pages = new(services, registry);
        public Control Create(PageViewModelBase model)
        {
            var page = _pages.Create(model);
            page.Tag = "project-factory";
            return page;
        }
    }
    public sealed class ReplacementGameView(GamePresentationState state, SampleGame model, IGameNavigationService navigation) : SampleGameView(state)
    {
        public GamePresentationState InjectedState { get; } = state;
        public SampleGame InjectedModel { get; } = model;
        public IGameNavigationService InjectedNavigation { get; } = navigation;
    }
    public sealed class NotebookView : UserControl
    {
        public NotebookView() => Content = new TextBlock { Text = "Notebook" };
    }

    private sealed class Session : IGameSessionService, IPreparedGameSessionService, ISaveSlotNamingSession
    {
        public string GameTitle => "Project template";
        public string StatusMessage => "";
        public bool IsReady => true;
        public bool IsPlaying => Begins > 0;
        public bool CanContinue => true;
        public int Begins { get; private set; }
        public Action? BeginCheck { get; set; }
        public Func<CancellationToken, Task>? Prepare { get; init; }
        private readonly ObservableCollection<GameSaveSlot> _slots = [new(0, default, "", true, false)];
        public ReadOnlyObservableCollection<GameSaveSlot> SaveSlots { get; }
        public Session() => SaveSlots = new(_slots);
        public int Saves { get; private set; }
        public bool CanRenameSlots => true;
        public Task<string> RenameSlotAsync(int slotIndex, string name, CancellationToken cancellationToken = default)
        { _slots[slotIndex] = _slots[slotIndex] with { Description = name }; return Task.FromResult(name); }
        public event PropertyChangedEventHandler? PropertyChanged;
        public Task PrepareNewGameAsync(CancellationToken cancellationToken = default) => Prepare?.Invoke(cancellationToken) ?? Task.CompletedTask;
        public Task PrepareContinueAsync(CancellationToken cancellationToken = default) => PrepareNewGameAsync(cancellationToken);
        public Task PrepareLoadAsync(int slotIndex, CancellationToken cancellationToken = default) => PrepareNewGameAsync(cancellationToken);
        public Task BeginPreparedGameAsync(CancellationToken cancellationToken = default)
        {
            BeginCheck?.Invoke();
            Begins++;
            PropertyChanged?.Invoke(this, new(nameof(IsPlaying)));
            return Task.CompletedTask;
        }
        public Task StartNewGameAsync(CancellationToken cancellationToken = default) => throw new AssertionException("Expected prepared startup.");
        public Task ContinueAsync(CancellationToken cancellationToken = default) => throw new AssertionException("Expected prepared startup.");
        public Task LoadAsync(int slotIndex, CancellationToken cancellationToken = default) => throw new AssertionException("Expected prepared startup.");
        public Task SaveAsync(int slotIndex, CancellationToken cancellationToken = default)
        { Saves++; _slots[slotIndex] = new(slotIndex, DateTime.UtcNow, "Project save", false, false); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
