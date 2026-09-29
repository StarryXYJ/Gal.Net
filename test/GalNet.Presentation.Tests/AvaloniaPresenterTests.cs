using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Core.Scene;
using GalNet.Presentation.Abstractions.View;
using GalNet.Rendering.Scene;

namespace GeneralTest.Presentation;

public sealed class AvaloniaPresenterTests
{
    [Test]
    public void PageViewComposesRatherThanImplementsPresenterPorts()
    {
        var pageViewType = typeof(AvaloniaGamePageView);
        var presenterTypes = new[]
        {
            typeof(IDialoguePresenter),
            typeof(IChoicePresenter),
            typeof(ILayerPresenter),
            typeof(IAnimationPresenter),
            typeof(IParticlePresenter)
        };

        Assert.Multiple(() =>
        {
            Assert.That(pageViewType.GetInterfaces(), Does.Not.Contain(typeof(IDialoguePresenter)));
            Assert.That(pageViewType.GetInterfaces(), Does.Not.Contain(typeof(IChoicePresenter)));
            Assert.That(pageViewType.GetInterfaces(), Does.Not.Contain(typeof(ILayerPresenter)));
            Assert.That(pageViewType.GetInterfaces(), Does.Not.Contain(typeof(IAnimationPresenter)));
            Assert.That(pageViewType.GetInterfaces(), Does.Not.Contain(typeof(IParticlePresenter)));
            foreach (var presenterType in presenterTypes)
                Assert.That(
                    pageViewType.GetProperties().Any(property => property.PropertyType == presenterType),
                    Is.True,
                    $"Missing {presenterType.Name} composition property.");
        });
    }

    [Test]
    public void LayerPresenterDispatchesACompleteLayerState()
    {
        var state = new GamePageViewModel(new NoOpGameNavigationService());
        var dispatcher = new ImmediateDispatcher();
        var presenter = new AvaloniaLayerPresenter(state, new NoTextureLayerFactory(), dispatcher);

        presenter.ShowLayer(new LayerRenderRequest(
            "backdrop",
            "unused",
            new LayerTransform { X = 12, Y = 24, ScaleX = 2, ScaleY = 3 },
            4,
            LayerDisplayMode.Fill,
            .75f,
            "#102030"));

        var layer = state.Layers.Single();
        Assert.Multiple(() =>
        {
            Assert.That(dispatcher.DispatchCount, Is.EqualTo(1));
            Assert.That(layer.HandleId, Is.EqualTo("backdrop"));
            Assert.That(layer.X, Is.EqualTo(12));
            Assert.That(layer.Y, Is.EqualTo(24));
            Assert.That(layer.ScaleX, Is.EqualTo(2));
            Assert.That(layer.ScaleY, Is.EqualTo(3));
            Assert.That(layer.Z, Is.EqualTo(4));
            Assert.That(layer.Opacity, Is.EqualTo(.75f));
        });
    }

    [Test]
    public async Task AnimationPresenterAppliesACompletedReplaceAnimation()
    {
        var state = new GamePageViewModel(new NoOpGameNavigationService());
        state.SetLayer("hero", new SceneLayerItem { HandleId = "hero", Opacity = .2f });
        var presenter = new AvaloniaAnimationPresenter(state, new ImmediateDispatcher());

        var outcome = await presenter.AnimateAsync(new AnimationRequest
        {
            PlaybackHandleId = "fade",
            HandleId = "hero",
            Property = "opacity",
            From = .2f,
            To = .8f,
            DurationSeconds = 0,
            Curve = AnimationCurves.Linear,
            LoopMode = AnimationLoopMode.Once
        }, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(AnimationOutcome.Completed));
            Assert.That(state.Layers.Single().Opacity, Is.EqualTo(.8f).Within(.001));
        });
    }

    private sealed class ImmediateDispatcher : IAvaloniaUiDispatcher
    {
        public int DispatchCount { get; private set; }

        public void Dispatch(Action action)
        {
            DispatchCount++;
            action();
        }

        public Task InvokeAsync(Func<Task> action) => action();
        public Task<T> InvokeAsync<T>(Func<Task<T>> action) => action();
    }

    private sealed class NoTextureLayerFactory : IGamePageLayerFactory
    {
        public SceneTexture ResolveTexture(string assetId) =>
            throw new InvalidOperationException("A solid-color layer must not resolve a texture.");
    }

    private sealed class NoOpGameNavigationService : IGameNavigationService
    {
        public PageViewModelBase? CurrentViewModel => null;
        public bool CanGoBack => false;
        public event EventHandler? CurrentViewModelChanged { add { } remove { } }
        public event EventHandler<GameNavigationChangedEventArgs>? Navigated { add { } remove { } }

        public void Navigate<TViewModel>(NavigationTransition transition = NavigationTransition.CrossFade)
            where TViewModel : PageViewModelBase
        {
        }

        public Task NavigateAsync(
            PageViewModelBase viewModel,
            NavigationTransition transition = NavigationTransition.CrossFade,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task NavigateAsync<TViewModel, TArgs>(
            TArgs args,
            NavigationTransition transition = NavigationTransition.CrossFade,
            CancellationToken cancellationToken = default)
            where TViewModel : PageViewModelBase, IActivatablePageViewModel<TArgs> => Task.CompletedTask;

        public void ResetTo<TViewModel>(NavigationTransition transition = NavigationTransition.CrossFade)
            where TViewModel : PageViewModelBase
        {
        }

        public Task ResetToAsync<TViewModel>(
            NavigationTransition transition = NavigationTransition.CrossFade,
            CancellationToken cancellationToken = default)
            where TViewModel : PageViewModelBase => Task.CompletedTask;

        public void GoBack(NavigationTransition transition = NavigationTransition.CrossFade)
        {
        }
    }
}
