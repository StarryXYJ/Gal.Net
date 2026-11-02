using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace GalNet.Sample.CrossPlatform.Views;

public sealed class AdaptiveCardGrid : UniformGrid
{
    protected override Size MeasureOverride(Size availableSize)
    {
        if (double.IsFinite(availableSize.Width))
        {
            var maximum = this.FindResource("GalNet.Theme.CardColumns") is int configured ? Math.Max(1, configured) : 4;
            var columns = Math.Clamp((int)(availableSize.Width / 240), 1, maximum);
            SetCurrentValue(ColumnsProperty, columns);
        }
        return base.MeasureOverride(availableSize);
    }
}
