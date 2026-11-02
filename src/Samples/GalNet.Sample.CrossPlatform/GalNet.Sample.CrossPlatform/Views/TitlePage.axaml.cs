using Avalonia.Controls;
using Avalonia;

namespace GalNet.Sample.CrossPlatform.Views;

public partial class TitlePage : UserControl
{
    public TitlePage() => InitializeComponent();

    protected override Size MeasureOverride(Size availableSize)
    {
        if (PlayerLayout.GetIsMobile(this))
        {
            TitleLayout.Margin = new Thickness(24, 16);
            if (TitleLayout.ColumnDefinitions[1].Width.Value != 0)
                TitleLayout.ColumnDefinitions = new("*,0");
            TitleMenu.Margin = new Thickness(0, 12, 0, 0);
            TitleMenu.MaxWidth = 340;
            TitleMenu.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left;
            TitleButtons.Margin = new Thickness(0);
        }
        return base.MeasureOverride(availableSize);
    }
}
