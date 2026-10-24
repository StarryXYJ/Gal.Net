using System.Globalization;

namespace GalNet.Core.Scene;

/// <summary>Portable scalar animation declarations shared by authoring and rendering.</summary>
public static class ShaderEffectAnimationProperties
{
    public static IReadOnlyList<AnimatableProperty> Create(ShaderEffectDescriptor descriptor) => Array.AsReadOnly(descriptor.Parameters
        .Where(parameter => parameter.Animatable && parameter.Kind is ShaderEffectParameterKind.Float or ShaderEffectParameterKind.Integer)
        .Select(parameter => new AnimatableProperty(parameter.Name, AnimationValueKind.Float,
            parameter.Minimum is { } minimum ? (float)minimum : null,
            parameter.Maximum is { } maximum ? (float)maximum : null,
            float.TryParse(parameter.DefaultValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && float.IsFinite(value) ? value : null))
        .ToArray());
}
