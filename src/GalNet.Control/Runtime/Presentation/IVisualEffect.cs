using GalNet.Core.View;

namespace GalNet.Control.Runtime.Presentation;

/// <summary>Internal Avalonia implementation contract for an effect registered by <see cref="EffectRegistry"/>.</summary>
public interface IVisualEffect
{
    string Name { get; }
    void Start(IGameView view, IReadOnlyDictionary<string, object> parameters);
    void Stop(IGameView view);
}
