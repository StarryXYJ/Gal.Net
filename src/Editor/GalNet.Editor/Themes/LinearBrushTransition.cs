using Avalonia.Animation;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace GalNet.Editor.Themes;

/// <summary>Premultiplied interpolation prevents transparent white from flashing on dark surfaces.</summary>
public sealed class LinearBrushTransition : InterpolatingTransitionBase<IBrush?>
{
    protected override IBrush? Interpolate(double progress, IBrush? oldValue, IBrush? newValue) =>
        Blend(progress, oldValue, newValue);

    public static IBrush? Blend(double progress, IBrush? oldValue, IBrush? newValue)
    {
        if (progress <= 0) return oldValue;
        if (progress >= 1) return newValue;
        if (oldValue is not null and not ISolidColorBrush || newValue is not null and not ISolidColorBrush)
            return newValue;
        var from = oldValue as ISolidColorBrush;
        var to = newValue as ISolidColorBrush;
        var a = (from?.Color.A ?? 0) / 255d * (from?.Opacity ?? 1);
        var b = (to?.Color.A ?? 0) / 255d * (to?.Opacity ?? 1);
        var alpha = a + (b - a) * progress;
        byte Channel(byte x, byte y) => alpha <= 0 ? (byte)0 : (byte)Math.Clamp(
            Math.Round((x * a * (1 - progress) + y * b * progress) / alpha), 0, 255);
        return new ImmutableSolidColorBrush(Color.FromArgb((byte)Math.Clamp(Math.Round(alpha * 255), 0, 255),
            Channel(from?.Color.R ?? 0, to?.Color.R ?? 0), Channel(from?.Color.G ?? 0, to?.Color.G ?? 0),
            Channel(from?.Color.B ?? 0, to?.Color.B ?? 0)));
    }
}
