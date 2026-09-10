using GalNet.Core.Scene;

namespace GalNet.Runtime.Handlers;

internal static class LayerAnimationProperties
{
    public static bool TryApply(Layer layer, string property, float value)
    {
        switch (property)
        {
            case "transform.x": layer.Transform.X = value; return true;
            case "transform.y": layer.Transform.Y = value; return true;
            case "transform.rotationDegrees": layer.Transform.RotationDegrees = value; return true;
            case "transform.scaleX" when value > 0: layer.Transform.ScaleX = value; return true;
            case "transform.scaleY" when value > 0: layer.Transform.ScaleY = value; return true;
            case "opacity" when value is >= 0 and <= 1: layer.Opacity = value; return true;
            default: return false;
        }
    }

    public static bool IsValid(string property, float value) => property switch
    {
        "transform.x" or "transform.y" or "transform.rotationDegrees" => true,
        "transform.scaleX" or "transform.scaleY" => value > 0,
        "opacity" => value is >= 0 and <= 1,
        _ => false
    };
}
