using Avalonia.Controls;
using Avalonia;

namespace GalNet.Sample.CrossPlatform.Views;

public partial class TitlePage : UserControl
{
    public TitlePage() => InitializeComponent();

    protected override Size MeasureOverride(Size availableSize)
    {
        if (double.IsFinite(availableSize.Width) && double.IsFinite(availableSize.Height))
        {
            var portrait = PlayerLayout.IsPortrait(availableSize);
            TitleLayout.ColumnDefinitions[0].Width = portrait ? new GridLength(1, GridUnitType.Star) : new GridLength(380);
            TitleLayout.ColumnDefinitions[1].Width = portrait ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            TitleLayout.Margin = portrait ? new Thickness(32, 48) : new Thickness(64, 48);
            TitleMenu.MaxWidth = 380;
            TitleMenu.Width = 380;
            TitleMenu.HorizontalAlignment = portrait ? global::Avalonia.Layout.HorizontalAlignment.Center : global::Avalonia.Layout.HorizontalAlignment.Stretch;
            GameTitleText.TextAlignment = portrait ? global::Avalonia.Media.TextAlignment.Center : global::Avalonia.Media.TextAlignment.Left;
            TitleProgress.HorizontalAlignment = portrait ? global::Avalonia.Layout.HorizontalAlignment.Center : global::Avalonia.Layout.HorizontalAlignment.Left;
            var titleSize = (double?)this.FindResource("GalNet.Theme.TitleSize") ?? 64;
            GameTitleText.FontSize = titleSize;
            var menuSize = (double?)this.FindResource("GalNet.Theme.MenuFontSize") ?? 20;
            foreach (var button in TitleButtons.Children.OfType<Button>())
            {
                button.FontSize = menuSize;
                button.MinHeight = 46;
                button.Padding = new Thickness(14, 7);
            }
        }
        return base.MeasureOverride(availableSize);
    }
}
