namespace GalNet.Core.Scene;

/// <summary>Center-anchored transform in logical game-canvas pixels and degrees.</summary>
public sealed class LayerTransform
{
    [Animatable("transform.x")]
    public float X { get; set; }
    [Animatable("transform.y")]
    public float Y { get; set; }
    [Animatable("transform.rotationDegrees")]
    public float RotationDegrees { get; set; }
    [Animatable("transform.scaleX", Minimum = 0.001f)]
    public float ScaleX { get; set; } = 1f;
    [Animatable("transform.scaleY", Minimum = 0.001f)]
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
