using System.Globalization;
using System.Text.Json;
using GalNet.Core.Parameters;

namespace GalNet.Contracts.Builtins.Scene;

/// <summary>Shared wire fields for shader forms and read-only property analysis.</summary>
public static class ShaderEffectFields
{
    public static FieldDescriptor Create(ShaderEffectParameterDescriptor parameter)
    {
        var type = parameter.Kind switch
        {
            ShaderEffectParameterKind.Float => typeof(float),
            ShaderEffectParameterKind.Integer => typeof(int),
            ShaderEffectParameterKind.Boolean => typeof(bool),
            ShaderEffectParameterKind.Vector2 or ShaderEffectParameterKind.Vector3 or ShaderEffectParameterKind.Vector4 => typeof(float[]),
            _ => typeof(string)
        };
        var constraints = new Dictionary<string, object>();
        if (parameter.Minimum is { } minimum && type != typeof(float[])) constraints["minimum"] = minimum;
        if (parameter.Maximum is { } maximum && type != typeof(float[])) constraints["maximum"] = maximum;
        if (parameter.Kind == ShaderEffectParameterKind.Enum && parameter.Options is { Count: > 0 } options)
            constraints["enum"] = options;
        if (parameter.Kind is ShaderEffectParameterKind.Vector2 or ShaderEffectParameterKind.Vector3 or ShaderEffectParameterKind.Vector4)
            constraints["minItems"] = constraints["maxItems"] = parameter.Kind switch
            {
                ShaderEffectParameterKind.Vector2 => 2,
                ShaderEffectParameterKind.Vector3 => 3,
                _ => 4
            };
        var descriptor = new ValueDescriptor(type, allowNull: false, constraints: JsonSerializer.SerializeToElement(constraints));
        var editor = parameter.Kind switch
        {
            ShaderEffectParameterKind.Texture => "entry.imageasset",
            ShaderEffectParameterKind.Color => "shader.color",
            ShaderEffectParameterKind.Vector2 => "shader.vector2",
            ShaderEffectParameterKind.Vector3 => "shader.vector3",
            ShaderEffectParameterKind.Vector4 => "shader.vector4",
            ShaderEffectParameterKind.Boolean => ValueEditorIds.Toggle,
            ShaderEffectParameterKind.Enum => ValueEditorIds.Choice,
            ShaderEffectParameterKind.Float or ShaderEffectParameterKind.Integer => ValueEditorIds.Numeric,
            _ => null
        };
        var fallback = Default(parameter, descriptor);
        return new(parameter.Name, descriptor, parameter.Required, fallback, new EditorHints(editor, parameter.Step),
            resourceTypeId: parameter.Kind == ShaderEffectParameterKind.Texture ? "sprite" : null);
    }

    private static JsonElement? Default(ShaderEffectParameterDescriptor parameter, ValueDescriptor descriptor)
    {
        if (parameter.DefaultValue is not { } text) return null;
        try
        {
            object value = parameter.Kind switch
            {
                ShaderEffectParameterKind.Float => float.Parse(text, CultureInfo.InvariantCulture),
                ShaderEffectParameterKind.Integer => int.Parse(text, CultureInfo.InvariantCulture),
                ShaderEffectParameterKind.Boolean => bool.Parse(text),
                ShaderEffectParameterKind.Vector2 or ShaderEffectParameterKind.Vector3 or ShaderEffectParameterKind.Vector4 => JsonSerializer.Deserialize<float[]>(text)!,
                _ => text
            };
            var json = JsonSerializer.SerializeToElement(value, descriptor.ValueType, descriptor.JsonOptions);
            return ValueValidation.Validate(descriptor, json) is null ? json : null;
        }
        catch (Exception error) when (error is JsonException or FormatException or OverflowException or ArgumentException)
        { return null; }
    }
}
