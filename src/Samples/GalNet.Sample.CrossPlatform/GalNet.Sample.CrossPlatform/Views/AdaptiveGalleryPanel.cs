using Avalonia;
using Avalonia.Controls;

namespace GalNet.Sample.CrossPlatform.Views;

public sealed class AdaptiveGalleryPanel : WrapPanel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = PlayerLayout.GetIsMobile(this) && double.IsFinite(availableSize.Width)
            ? availableSize.Width / Math.Max(1, (int)(availableSize.Width / 240)) : 260;
        ItemWidth = width;
        ItemHeight = width * 190 / 260;
        return base.MeasureOverride(availableSize);
    }
}
