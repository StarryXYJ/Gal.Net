using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using GalNet.Avalonia.GameHost;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Page;
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
            Assert.That(navigation.CurrentViewModel, Is.TypeOf<GalNet.Avalonia.GameView.ViewModels.SettingsPageViewModel>());
            Assert.That(advances, Is.Zero);
        }
        finally { window.Close(); }
    });

    private static ServiceCollection Services(Session session)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IGameSessionService>(session);
        services.AddScoped<Notebook>();
        SampleCustomization.ConfigureServices(services, views => views.Register<Notebook, NotebookView>());
        services.AddScoped<GalNet.Avalonia.GameView.ViewModels.TitlePageViewModel>(_ => throw new AssertionException("Legacy title VM must not be used."));
        services.AddScoped<GalNet.Avalonia.GameView.ViewModels.LoadingPageViewModel>(_ => throw new AssertionException("Legacy loading VM must not be used."));
        services.AddScoped<GalNet.Avalonia.GameView.ViewModels.GamePageViewModel>(_ => throw new AssertionException("Legacy game VM must not be used."));
        return services;
    }

    public sealed class Notebook : PageViewModelBase;
    public sealed class NotebookView : UserControl
    {
        public NotebookView() => Content = new TextBlock { Text = "Notebook" };
    }

    private sealed class Session : IGameSessionService, IPreparedGameSessionService
    {
        public string GameTitle => "Project template";
        public string StatusMessage => "";
        public bool IsReady => true;
        public bool IsPlaying => Begins > 0;
        public bool CanContinue => true;
        public int Begins { get; private set; }
        public Action? BeginCheck { get; set; }
        public Func<CancellationToken, Task>? Prepare { get; init; }
        public ReadOnlyObservableCollection<GameSaveSlot> SaveSlots { get; } = new(new ObservableCollection<GameSaveSlot>());
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
        public Task SaveAsync(int slotIndex, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
