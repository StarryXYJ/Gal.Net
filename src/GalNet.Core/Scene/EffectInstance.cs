namespace GalNet.Core.Scene;

/// <summary>A live effect target whose numeric parameters can be driven by the common animation system.</summary>
public sealed class EffectInstance : AnimatableSceneInstance
{
    private readonly Dictionary<string, float> _values = new(StringComparer.Ordinal)
    {
        ["progress"] = 0
    };

    public override string Id { get; init; } = "";
    public string EffectId { get; init; } = "";
    public string TargetHandleId { get; init; } = "";
    public string Parameters { get; init; } = "{}";
    public override IReadOnlyList<AnimatableProperty> AnimatableProperties { get; } = [new("progress", AnimationValueKind.Float, 0, 1)];
    public override bool TryGetAnimationValue(string propertyName, out float value) => _values.TryGetValue(propertyName, out value);
    public override bool TrySetAnimationValue(string propertyName, float value, out string? error)
    {
        var property = AnimatableProperties.FirstOrDefault(candidate => candidate.Name == propertyName);
        if (property is null || !property.Accepts(value)) { error = $"Unknown or invalid effect animation property '{propertyName}'."; return false; }
        _values[propertyName] = value; error = null; return true;
    }
}
