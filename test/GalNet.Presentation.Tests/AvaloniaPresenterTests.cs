using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Core.Execution;
using GalNet.Presentation.Abstractions.View;
using GalNet.Rendering.Scene;

namespace GeneralTest.Presentation;

public sealed class AvaloniaPresenterTests
{
    [Test]
    public void PresenterCollectionComposesRatherThanImplementsPresenterPorts()
    {
        var pageViewType = typeof(AvaloniaGamePresenters);
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
            Assert.That(pageViewType.GetEvent("AdvanceRequested"), Is.Null);
            Assert.That(typeof(AvaloniaDialoguePresenter).GetEvent("AdvanceRequested"), Is.Null);
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

    [Test]
    public async Task Stopping_looping_shake_removes_only_its_offset_and_allows_reuse()
    {
        var state = new GamePageViewModel(new NoOpGameNavigationService());
        state.SetLayer("hero", new SceneLayerItem { HandleId = "hero", X = 37 });
        using var presenter = new AvaloniaAnimationPresenter(state, new ImmediateDispatcher());
        var timeline = new TimelineDefinition
        {
            PlaybackHandleId = "quake",
            DurationFrames = 60,
            FrameRate = 60,
            LoopMode = AnimationLoopMode.Loop,
            Tracks = [new() { HandleId = "hero", Property = "transform.x", BlendMode = AnimationBlendMode.Additive,
                Keys = [new() { Frame = 0, Value = 0 }, new() { Frame = 30, Value = 16 }, new() { Frame = 60, Value = 0 }] }]
        };
        for (var round = 0; round < 2; round++)
        {
            var playback = presenter.PlayTimelineAsync(timeline, CancellationToken.None);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            while (state.Layers.Single().X < 37.1) await Task.Delay(8, timeout.Token);
            Assert.That(presenter.CompleteAnimationImmediately("quake"), Is.True);
            var result = await playback.WaitAsync(timeout.Token);
            Assert.That(result.Outcome, Is.EqualTo(AnimationOutcome.Skipped));
            Assert.That(state.Layers.Single().X, Is.EqualTo(37).Within(.001));
        }
    }

    [Test]
    public async Task DrainedParticleRemovalIsPostedUntilAfterTheRenderPass()
    {
        var state = new GamePageViewModel(new NoOpGameNavigationService());
        var dispatcher = new QueuedDispatcher();
        using var presenter = new AvaloniaParticlePresenter(state, new NullTextureLayerFactory(), dispatcher);
        var request = new ParticleEmitterRequest("ending-particles", new ParticleEmitterDefinition("spark"), 0);

        await presenter.StartParticleEmitterAsync(request, CancellationToken.None);
        await presenter.StopParticleEmitterAsync(request.InstanceId, CancellationToken.None);
        var drained = state.SceneRenderables.OfType<ParticleEmitter>().Single();

        drained.Update(new SceneFrameContext(TimeSpan.Zero, TimeSpan.Zero, new SkiaSharp.SKSize(1, 1)));

        Assert.Multiple(() =>
        {
            Assert.That(state.SceneRenderables, Has.Count.EqualTo(1), "The render pass must not mutate its renderable collection.");
            Assert.That(dispatcher.PostedCount, Is.EqualTo(1));
        });

        await presenter.StartParticleEmitterAsync(request, CancellationToken.None);
        dispatcher.RunPosted();

        Assert.That(state.SceneRenderables.OfType<ParticleEmitter>().Select(emitter => emitter.HandleId),
            Is.EquivalentTo(new[] { request.InstanceId }), "A late drain must not remove a replacement emitter with the same ID.");
    }

    private sealed class ImmediateDispatcher : IAvaloniaUiDispatcher
    {
        public int DispatchCount { get; private set; }

        public void Dispatch(Action action)
        {
            DispatchCount++;
            action();
        }

        public void Post(Action action) => action();

        public Task InvokeAsync(Func<Task> action) => action();
        public Task<T> InvokeAsync<T>(Func<Task<T>> action) => action();
    }

    private sealed class QueuedDispatcher : IAvaloniaUiDispatcher
    {
        private readonly Queue<Action> _posted = [];

        public int PostedCount => _posted.Count;

        public void Dispatch(Action action) => action();
        public void Post(Action action) => _posted.Enqueue(action);
        public Task InvokeAsync(Func<Task> action) => action();
        public Task<T> InvokeAsync<T>(Func<Task<T>> action) => action();

        public void RunPosted()
        {
            while (_posted.TryDequeue(out var action)) action();
        }
    }

    private sealed class NullTextureLayerFactory : IGamePageLayerFactory
    {
        public SceneTexture ResolveTexture(string assetId) => null!;
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
