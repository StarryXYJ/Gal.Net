using Avalonia.Controls;
using Avalonia.Media;
using GalNet.Control.Abstraction.UI;
using GalNet.Control.Widget;
using Avalonia.Data;

namespace GalNet.Control.Widget.BuiltIn;

public sealed class DefaultTitleButtonConfig : PresentationConfig
{
    public DefaultTitleButtonConfig() { FontSize = 20; Width = 260; Height = 50; }
}

/// <summary>
/// Title screen menu button.
/// </summary>
public sealed class DefaultTitleButtonTemplate : Button
{
    public DefaultTitleButtonTemplate(DefaultTitleButtonConfig? config = null)
    {
        var cfg = config ?? new DefaultTitleButtonConfig();
        FontSize = cfg.FontSize ?? 20;
        MinWidth = cfg.Width ?? 260;
        MinHeight = cfg.Height ?? 50;
        HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center;
        Bind(BackgroundProperty, PaletteBinding.Create(this, "HighlightBackground"));
        Bind(ForegroundProperty, PaletteBinding.Create(this, "FontHighlightColor"));

        Bind(ContentProperty, new Binding(nameof(ButtonWidgetViewModel.Text)));
        Bind(IsEnabledProperty, new Binding(nameof(ButtonWidgetViewModel.IsEnabled)));
        Bind(CommandProperty, new Binding(nameof(ButtonWidgetViewModel.Command)));
    }
}
