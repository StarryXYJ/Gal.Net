namespace GalNet.Core.Scene;

/// <summary>Marks a scene property as selectable by the authoring animation UI.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AnimatableAttribute(string name) : Attribute
{
    public string Name { get; } = name;
    // Attribute arguments cannot be nullable. NaN means "no bound".
    public float Minimum { get; init; } = float.NaN;
    public float Maximum { get; init; } = float.NaN;
}
