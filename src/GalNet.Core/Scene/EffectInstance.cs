namespace GalNet.Core.Scene;

/// <summary>A live effect target whose numeric parameters can be driven by the common animation system.</summary>
public sealed class EffectInstance : AnimatableSceneInstance
{
    private readonly Dictionary<string, float> _values = new(StringComparer.Ordinal) { ["progress"] = 0 };
    private readonly List<AnimatableProperty> _properties = [new("progress", AnimationValueKind.Float, 0, 1)];

    public override string Id { get; init; } = "";
    public string EffectId { get; init; } = "";
    public string TargetHandleId { get; init; } = "";
    public int Order { get; init; }
    public string Parameters { get; init; } = "{}";
    public override IReadOnlyList<AnimatableProperty> AnimatableProperties => _properties;
    public IReadOnlyDictionary<string, float> AnimationValues => _values;
    /// <summary>Registers an Effect-owned float property without imposing platform knowledge on Runtime.</summary>
    public AnimatableProperty EnsureAnimationProperty(string propertyName)
    {
        var existing = _properties.FirstOrDefault(property => property.Name == propertyName);
        if (existing is not null) return existing;
        if (string.IsNullOrWhiteSpace(propertyName)) throw new InvalidDataException("Effect animation property cannot be empty.");
        var property = new AnimatableProperty(propertyName, AnimationValueKind.Float);
        _properties.Add(property);
        _values[propertyName] = 0;
        return property;
    }
    public override bool TryGetAnimationValue(string propertyName, out float value) => _values.TryGetValue(propertyName, out value);
    public override bool TrySetAnimationValue(string propertyName, float value, out string? error)
    {
        var property = EnsureAnimationProperty(propertyName);
        if (!property.Accepts(value)) { error = $"Invalid effect animation property '{propertyName}'."; return false; }
        _values[propertyName] = value; error = null; return true;
    }

    /// <summary>Restores values previously committed by the common animation system.</summary>
    public void RestoreAnimationValues(IEnumerable<KeyValuePair<string, float>> values)
    {
        foreach (var (propertyName, value) in values)
            if (!TrySetAnimationValue(propertyName, value, out var error))
                throw new InvalidDataException(error);
    }
}
