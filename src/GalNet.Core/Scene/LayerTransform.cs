namespace GalNet.Core.Scene;

/// <summary>Center-anchored transform in logical game-canvas pixels and degrees.</summary>
public sealed class LayerTransform
{
    public float X { get; set; }
    public float Y { get; set; }
    public float RotationDegrees { get; set; }
    public float ScaleX { get; set; } = 1f;
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
