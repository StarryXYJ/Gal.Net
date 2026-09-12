namespace GalNet.Core.View;

/// <summary>
/// Host-facing presentation facade. It is only a convenience composition of focused
/// contracts; runtime code should depend on the smallest contract it needs.
/// </summary>
public interface IGameView :
    ILayerView,
    IAnimationView,
    IControlView,
    IAudioView,
    IVideoView,
    IEffectView,
    ITypewriterView,
    IInteractionView
{
}
