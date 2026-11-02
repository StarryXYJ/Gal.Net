using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace GalNet.Sample.CrossPlatform.Views;

public sealed class AdaptiveSaveGrid : UniformGrid
{
    protected override Size MeasureOverride(Size availableSize)
    {
        if (double.IsFinite(availableSize.Width))
        {
            var columns = PlayerLayout.GetIsMobile(this)
                ? Math.Clamp((int)(availableSize.Width / 240), 1, 4)
                : (int?)this.FindResource("GalNet.Theme.SaveColumns") ?? 4;
            SetCurrentValue(ColumnsProperty, columns);
        }
        return base.MeasureOverride(availableSize);
    }
}
