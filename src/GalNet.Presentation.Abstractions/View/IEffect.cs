namespace GalNet.Core.View;

/// <summary>
/// Legacy host-side effect plug-in contract. It remains during the transition
/// to <see cref="IEffectView.StartEffectAsync"/>.
/// </summary>
public interface IEffect
{
    string Name { get; }
    void Start(IGameView view, IReadOnlyDictionary<string, object> parameters);
    void Stop(IGameView view);
}
