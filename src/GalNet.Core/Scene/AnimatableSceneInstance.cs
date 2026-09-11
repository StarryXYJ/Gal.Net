namespace GalNet.Core.Scene;

/// <summary>The value representation an animation property currently accepts.</summary>
public enum AnimationValueKind
{
    Float
}

/// <summary>Immutable metadata for one property an editor or runtime may animate.</summary>
public sealed record AnimatableProperty(
    string Name,
    AnimationValueKind ValueKind,
    float? Minimum = null,
    float? Maximum = null)
{
    public bool Accepts(float value) =>
        float.IsFinite(value) &&
        (!Minimum.HasValue || value >= Minimum.Value) &&
        (!Maximum.HasValue || value <= Maximum.Value);
}

/// <summary>
/// A live scene instance that owns its animation property metadata and mutation rules.
/// The property collection is safe to expose to editor tooling; mutation remains encapsulated.
/// </summary>
public abstract class AnimatableSceneInstance : ISceneInstance
{
    /// <summary>Stable opaque handle. It is assigned while creating or deserializing an instance.</summary>
    public abstract string Id { get; init; }
    public abstract IReadOnlyList<AnimatableProperty> AnimatableProperties { get; }
    public abstract bool TryGetAnimationValue(string propertyName, out float value);
    public abstract bool TrySetAnimationValue(string propertyName, float value, out string? error);
}
