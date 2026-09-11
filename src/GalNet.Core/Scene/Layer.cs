using System.Text.Json.Serialization;

namespace GalNet.Core.Scene;

/// <summary>
/// Resource-backed scene instance. Its Id is a stable opaque handle; z values sort front-to-back.
/// </summary>
public sealed class Layer : AnimatableSceneInstance
{
    public static IReadOnlyList<AnimatableProperty> AnimationProperties { get; } =
    [
        new("transform.x", AnimationValueKind.Float),
        new("transform.y", AnimationValueKind.Float),
        new("transform.rotationDegrees", AnimationValueKind.Float),
        new("transform.scaleX", AnimationValueKind.Float, Minimum: 0.001f),
        new("transform.scaleY", AnimationValueKind.Float, Minimum: 0.001f),
        new("opacity", AnimationValueKind.Float, Minimum: 0, Maximum: 1)
    ];

    /// <summary>Stable scene-instance handle. Editors generate this as an opaque GUID.</summary>
    public override string Id { get; init; } = "";

    /// <summary>资源 ID 引用</summary>
    public string AssetId { get; set; } = "";

    /// <summary>Optional #RRGGBB or #AARRGGBB solid color used instead of an image asset.</summary>
    public string? Color { get; set; }

    public LayerTransform Transform { get; set; } = new();

    /// <summary>z-index 层叠顺序，越大越靠前。背景建议 0，立绘建议 5~20</summary>
    public float Z { get; set; }

    public LayerDisplayMode DisplayMode { get; set; } = LayerDisplayMode.Native;

    public bool Visible { get; set; } = true;

    public float Opacity { get; set; } = 1;

    /// <summary>
    /// Effect instance handles attached to this layer. The effect itself remains the
    /// canonical owner of its target; this list is the layer-local rendering index.
    /// </summary>
    public List<string> EffectInstanceIds { get; set; } = [];

    public override IReadOnlyList<AnimatableProperty> AnimatableProperties => AnimationProperties;

    public override bool TryGetAnimationValue(string propertyName, out float value)
    {
        switch (propertyName)
        {
            case "transform.x": value = Transform.X; return true;
            case "transform.y": value = Transform.Y; return true;
            case "transform.rotationDegrees": value = Transform.RotationDegrees; return true;
            case "transform.scaleX": value = Transform.ScaleX; return true;
            case "transform.scaleY": value = Transform.ScaleY; return true;
            case "opacity": value = Opacity; return true;
            default: value = default; return false;
        }
    }

    public override bool TrySetAnimationValue(string propertyName, float value, out string? error)
    {
        var property = AnimationProperties.FirstOrDefault(item => item.Name == propertyName);
        if (property is null)
        {
            error = $"Unknown animation property '{propertyName}'.";
            return false;
        }
        if (!property.Accepts(value))
        {
            error = $"Animation property '{propertyName}' does not accept value '{value}'.";
            return false;
        }

        switch (propertyName)
        {
            case "transform.x": Transform.X = value; break;
            case "transform.y": Transform.Y = value; break;
            case "transform.rotationDegrees": Transform.RotationDegrees = value; break;
            case "transform.scaleX": Transform.ScaleX = value; break;
            case "transform.scaleY": Transform.ScaleY = value; break;
            case "opacity": Opacity = value; break;
            default: error = $"Unknown animation property '{propertyName}'."; return false;
        }

        error = null;
        return true;
    }

    /// <summary>Checks the portable hexadecimal color syntax supported by color layers.</summary>
    public static bool IsValidColor(string? value) => value is { Length: 7 or 9 } && value[0] == '#' && value[1..].All(Uri.IsHexDigit);

    // Read-only compatibility bridge for saves written before Transform existed.
    // [JsonPropertyName("X")]
    // [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    // public float? LegacyX { get => null; set { if (value is { } x) Transform.X = x; } }
    //
    // [JsonPropertyName("Y")]
    // [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    // public float? LegacyY { get => null; set { if (value is { } y) Transform.Y = y; } }
}
