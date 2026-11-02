using Avalonia.Controls;
using Avalonia;

namespace GalNet.Sample.CrossPlatform.Views;

public partial class TitlePage : UserControl
{
    public TitlePage() => InitializeComponent();

    protected override Size MeasureOverride(Size availableSize)
    {
        if (PlayerLayout.IsCompact(availableSize))
        {
            TitleLayout.Margin = new Thickness(24, 16);
            if (TitleLayout.ColumnDefinitions[1].Width.Value != 0)
                TitleLayout.ColumnDefinitions = new("*,0");
            TitleMenu.Margin = new Thickness(0, 12, 0, 0);
            TitleMenu.MaxWidth = 340;
            TitleMenu.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left;
            TitleButtons.Margin = new Thickness(0);
        }
        else
        {
            TitleLayout.Margin = new Thickness(48, 40);
            if (TitleLayout.ColumnDefinitions[0].Width.Value != 320)
                TitleLayout.ColumnDefinitions = new("320,*");
            TitleMenu.Margin = new Thickness(0, 32, 0, 0);
            TitleMenu.MaxWidth = double.PositiveInfinity;
            TitleMenu.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch;
            TitleButtons.Margin = new Thickness(0, 0, 16, 0);
        }
        return base.MeasureOverride(availableSize);
    }
}
