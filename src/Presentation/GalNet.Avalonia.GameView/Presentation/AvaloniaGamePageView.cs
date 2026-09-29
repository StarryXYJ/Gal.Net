using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Presentation.Abstractions.View;
using GalNet.Rendering.Scene;

namespace GalNet.Avalonia.GameView.Presentation;

/// <summary>Host-provided creation of layer visuals; the shared page never resolves files itself.</summary>
public interface IGamePageLayerFactory : ISceneTextureResolver
{
}

/// <summary>Composes the presentation ports used by one shared game page.</summary>
public sealed class AvaloniaGamePageView : IDisposable
{
    private readonly AvaloniaDialoguePresenter _dialogue;
    private readonly AvaloniaAnimationPresenter _animation;
    private readonly AvaloniaParticlePresenter _particles;

    public AvaloniaGamePageView(
        GamePageViewModel state,
        GamePage page,
        IGamePageLayerFactory layers,
        IAvaloniaUiDispatcher? dispatcher = null)
    {
        dispatcher ??= AvaloniaUiDispatcher.Instance;
        _dialogue = new AvaloniaDialoguePresenter(state, page, dispatcher);
        _animation = new AvaloniaAnimationPresenter(state, dispatcher);
        _particles = new AvaloniaParticlePresenter(state, layers, dispatcher);
        DialoguePresenter = _dialogue;
        ChoicePresenter = _dialogue;
        LayerPresenter = new AvaloniaLayerPresenter(state, layers, dispatcher);
        AnimationPresenter = _animation;
        ParticlePresenter = _particles;
    }

    public IDialoguePresenter DialoguePresenter { get; }
    public IChoicePresenter ChoicePresenter { get; }
    public ILayerPresenter LayerPresenter { get; }
    public IAnimationPresenter AnimationPresenter { get; }
    public IParticlePresenter ParticlePresenter { get; }
    public Task InitialPresentationReady => _dialogue.InitialPresentationReady;

    public event Action? AdvanceRequested
    {
        add => _dialogue.AdvanceRequested += value;
        remove => _dialogue.AdvanceRequested -= value;
    }

    public void CompleteInitialPresentation() => _dialogue.CompleteInitialPresentation();

    public void FailInitialPresentation(Exception exception) => _dialogue.FailInitialPresentation(exception);

    public void Dispose()
    {
        _particles.Dispose();
        _animation.Dispose();
        _dialogue.Dispose();
    }
}
