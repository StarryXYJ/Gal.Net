using Avalonia.Controls;
using Avalonia.Media;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Rendering.Scene;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Presentation;

public sealed class GameNavigationTests
{
    [Test]
    public void Navigation_reuses_scoped_view_models_and_preserves_history()
    {
        using var provider = CreateServices().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;
        var navigation = services.GetRequiredService<IGameNavigationService>();

        navigation.ResetTo<FirstPage>();
        var first = navigation.CurrentViewModel;
        navigation.Navigate<SecondPage>();

        Assert.Multiple(() =>
        {
            Assert.That(navigation.CanGoBack, Is.True);
            Assert.That(navigation.CurrentViewModel, Is.SameAs(services.GetRequiredService<SecondPage>()));
        });

        navigation.GoBack();
        Assert.That(navigation.CurrentViewModel, Is.SameAs(first));
    }

    [Test]
    public async Task Parameter_activation_completes_before_navigation()
    {
        using var provider = CreateServices().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var navigation = scope.ServiceProvider.GetRequiredService<IGameNavigationService>();

        await navigation.NavigateAsync<ActivatedPage, string>("payload");

        var page = (ActivatedPage)navigation.CurrentViewModel!;
        Assert.That(page.Argument, Is.EqualTo("payload"));
    }

    [Test]
    public void Navigation_reports_the_requested_transition_policy()
    {
        using var provider = CreateServices().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var navigation = scope.ServiceProvider.GetRequiredService<IGameNavigationService>();
        GameNavigationChangedEventArgs? observed = null;
        navigation.Navigated += (_, args) => observed = args;

        navigation.ResetTo<FirstPage>(NavigationTransition.None);

        Assert.Multiple(() =>
        {
            Assert.That(observed, Is.Not.Null);
            Assert.That(observed!.Transition, Is.EqualTo(NavigationTransition.None));
            Assert.That(observed.Current, Is.TypeOf<FirstPage>());
        });
    }

    [Test]
    public async Task Async_reset_does_not_replace_the_page_until_presentation_finishes()
    {
        using var provider = CreateServices().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var navigation = scope.ServiceProvider.GetRequiredService<IGameNavigationService>();
        navigation.ResetTo<FirstPage>(NavigationTransition.None);
        var change = navigation.ResetToAsync<SecondPage>(NavigationTransition.CrossFade);

        await change;
        Assert.That(navigation.CurrentViewModel, Is.TypeOf<SecondPage>());
    }

    [Test]
    public async Task Async_reset_uses_the_requested_transition()
    {
        var transitions = new RecordingTransitionCoordinator();
        using var provider = CreateServices(transitions).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var navigation = scope.ServiceProvider.GetRequiredService<IGameNavigationService>();

        navigation.ResetTo<FirstPage>(NavigationTransition.None);
        transitions.Events.Clear();

        await navigation.ResetToAsync<SecondPage>(NavigationTransition.CrossFade);

        Assert.That(transitions.Events, Is.EqualTo([
            "present:SecondPage:CrossFade"
        ]));
    }

    [Test]
    public async Task Async_reset_can_be_repeated_after_returning_to_the_previous_page()
    {
        var transitions = new RecordingTransitionCoordinator();
        using var provider = CreateServices(transitions).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var navigation = scope.ServiceProvider.GetRequiredService<IGameNavigationService>();

        navigation.ResetTo<FirstPage>(NavigationTransition.None);
        await navigation.ResetToAsync<SecondPage>(NavigationTransition.CrossFade);
        navigation.ResetTo<FirstPage>(NavigationTransition.CrossFade);
        transitions.Events.Clear();

        await navigation.ResetToAsync<SecondPage>(NavigationTransition.CrossFade);

        Assert.That(transitions.Events, Is.EqualTo([
            "present:SecondPage:CrossFade"
        ]));
    }

    [Test]
    public void Registry_override_controls_the_view_mapping_and_scope_reuses_the_view()
    {
        var registry = new PageViewRegistryBuilder();
        registry.Register<FirstPage, FirstView>();
        registry.Register<FirstPage, ReplacementView>();
        var services = CreateServices();
        services.AddSingleton(registry.Build());
        services.AddScoped<IPageViewFactory, PageViewFactory>();
        services.AddScoped<FirstView>();
        services.AddScoped<ReplacementView>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IPageViewFactory>();
        var page = scope.ServiceProvider.GetRequiredService<FirstPage>();

        var first = factory.Create(page);
        var second = factory.Create(page);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.TypeOf<ReplacementView>());
            Assert.That(second, Is.SameAs(first));
            Assert.That(first.DataContext, Is.SameAs(page));
        });
    }

    [Test]
    public void Registry_build_creates_an_immutable_mapping_snapshot()
    {
        var builder = new PageViewRegistryBuilder();
        builder.Register<FirstPage, FirstView>();
        var registry = builder.Build();

        Assert.That(registry.GetViewType(typeof(FirstPage)), Is.EqualTo(typeof(FirstView)));
        Assert.Throws<InvalidOperationException>(() => builder.Register<FirstPage, ReplacementView>());
    }

    [Test]
    public void First_advance_after_hiding_ui_only_restores_the_player_ui()
    {
        var page = new GamePageViewModel(new NoOpGameNavigationService());
        var advances = 0;
        page.AdvanceRequested += () => advances++;
        page.IsUiHidden = true;

        page.AdvanceCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(page.IsUiHidden, Is.False);
            Assert.That(advances, Is.Zero);
        });

        page.AdvanceCommand.Execute(null);
        Assert.That(advances, Is.EqualTo(1));
    }

    [Test]
    public void Dialogue_overlay_remains_available_with_a_missing_layer_fallback()
    {
        var page = new GamePageViewModel(new NoOpGameNavigationService());
        using var fallback = new SceneTexture(new DrawingImage());
        page.SetLayer("missing-layer-demo", new SceneLayerItem
        {
            HandleId = "missing-layer-demo",
            Texture = fallback,
            Z = 5
        });

        page.IsDialogueVisible = true;

        Assert.Multiple(() =>
        {
            Assert.That(page.Layers.Single().Texture, Is.SameAs(fallback));
            Assert.That(page.IsDialogueOverlayVisible, Is.True);
        });

        page.IsUiHidden = true;
        Assert.That(page.IsDialogueOverlayVisible, Is.False);
    }

    [Test]
    public async Task Advance_completes_the_current_player_wait()
    {
        var page = new GamePageViewModel(new NoOpGameNavigationService());
        page.AdvanceRequested += page.CompleteAdvance;
        var wait = page.WaitForAdvanceAsync(TestContext.CurrentContext.CancellationToken);

        page.AdvanceCommand.Execute(null);

        await wait.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Test]
    public async Task Selecting_a_choice_completes_the_choice_wait()
    {
        var page = new GamePageViewModel(new NoOpGameNavigationService());
        var choice = page.WaitForChoiceAsync(["Downtown", "Harbor"], TestContext.CurrentContext.CancellationToken);

        page.CompleteChoiceCommand.Execute(1);

        var selected = await choice.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Multiple(() =>
        {
            Assert.That(selected, Is.EqualTo(1));
            Assert.That(page.IsChoiceVisible, Is.False);
            Assert.That(page.Choices, Is.Empty);
        });
    }

    [Test]
    public void Disposing_scope_disposes_resolved_page_view_models()
    {
        DisposablePage.DisposeCount = 0;
        using var provider = CreateServices().BuildServiceProvider();
        var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IGameNavigationService>().ResetTo<DisposablePage>();

        scope.Dispose();

        Assert.That(DisposablePage.DisposeCount, Is.EqualTo(1));
    }

    private static ServiceCollection CreateServices(IGameNavigationTransitionCoordinator? transitions = null)
    {
        var services = new ServiceCollection();
        if (transitions is null)
        {
            services.AddScoped<GameNavigationTransitionCoordinator>();
            services.AddScoped<IGameNavigationTransitionCoordinator>(provider =>
                provider.GetRequiredService<GameNavigationTransitionCoordinator>());
        }
        else services.AddSingleton<IGameNavigationTransitionCoordinator>(transitions);
        services.AddScoped<IGameNavigationService, GameNavigationService>();
        services.AddScoped<FirstPage>();
        services.AddScoped<SecondPage>();
        services.AddScoped<ActivatedPage>();
        services.AddScoped<DisposablePage>();
        return services;
    }

    private sealed class FirstPage : PageViewModelBase;
    private sealed class SecondPage : PageViewModelBase;
    private sealed class ActivatedPage : PageViewModelBase, IActivatablePageViewModel<string>
    {
        public string? Argument { get; private set; }
        public Task ActivateAsync(string args, CancellationToken cancellationToken = default)
        {
            Argument = args;
            return Task.CompletedTask;
        }
    }
    private sealed class DisposablePage : PageViewModelBase, IDisposable
    {
        public static int DisposeCount { get; set; }
        public void Dispose() => DisposeCount++;
    }
    private sealed class FirstView : Control;
    private sealed class ReplacementView : Control;

    private sealed class RecordingTransitionCoordinator : IGameNavigationTransitionCoordinator
    {
        public List<string> Events { get; } = [];

        public Task PresentPageAsync(PageViewModelBase? viewModel, NavigationTransition transition, CancellationToken cancellationToken = default)
        {
            Events.Add($"present:{viewModel?.GetType().Name ?? "null"}:{transition}");
            return Task.CompletedTask;
        }


    }

    private sealed class NoOpGameNavigationService : IGameNavigationService
    {
        public PageViewModelBase? CurrentViewModel => null;
        public bool CanGoBack => false;
        public event EventHandler? CurrentViewModelChanged { add { } remove { } }
        public event EventHandler<GameNavigationChangedEventArgs>? Navigated { add { } remove { } }
        public void Navigate<TViewModel>(NavigationTransition transition = NavigationTransition.CrossFade) where TViewModel : PageViewModelBase { }
        public Task NavigateAsync<TViewModel, TArgs>(TArgs args, NavigationTransition transition = NavigationTransition.CrossFade, CancellationToken cancellationToken = default)
            where TViewModel : PageViewModelBase, IActivatablePageViewModel<TArgs> => Task.CompletedTask;
        public void ResetTo<TViewModel>(NavigationTransition transition = NavigationTransition.CrossFade) where TViewModel : PageViewModelBase { }
        public Task ResetToAsync<TViewModel>(NavigationTransition transition = NavigationTransition.CrossFade, CancellationToken cancellationToken = default)
            where TViewModel : PageViewModelBase => Task.CompletedTask;
        public void GoBack(NavigationTransition transition = NavigationTransition.CrossFade) { }
    }
}
