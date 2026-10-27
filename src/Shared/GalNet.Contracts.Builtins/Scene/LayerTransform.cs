using GalNet.Core.Parameters;

namespace GalNet.Contracts.Builtins.Scene;

/// <summary>Center-anchored transform in logical game-canvas pixels and degrees.</summary>
[GenerateFieldSchema]
public sealed partial class LayerTransform
{
    [Field(0, LabelKey = "Form.Transform.X")]
    public float X { get; set; } = 0f;
    [Field(1, LabelKey = "Form.Transform.Y")]
    public float Y { get; set; } = 0f;
    [Field(2, LabelKey = "Form.Transform.RotationDegrees")]
    public float RotationDegrees { get; set; } = 0f;
    [Field(3, LabelKey = "Form.Transform.ScaleX")]
    public float ScaleX { get; set; } = 1f;
    [Field(4, LabelKey = "Form.Transform.ScaleY")]
    public float ScaleY { get; set; } = 1f;

    public LayerTransform Clone() => new()
    {
        X = X,
        Y = Y,
        RotationDegrees = RotationDegrees,
        ScaleX = ScaleX,
        ScaleY = ScaleY
    };
}
