namespace GalNet.Core.View;

public interface IEffectView
{
    void ApplyTransition(string type, float durationSec);
    void ApplyEffect(string effectType, IReadOnlyDictionary<string, object> parameters);
    void StopEffect(string effectId);
}
