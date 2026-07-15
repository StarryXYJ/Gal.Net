using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GalNet.Control.Abstraction.UI;
using GalNet.Control.Widget;
using Avalonia.Data;

namespace GalNet.Control.Widget.BuiltIn;

public sealed class DefaultButtonConfig : PresentationConfig
{
    public DefaultButtonConfig() { FontSize = 16; Width = 160; Height = 40; }
    public string BackgroundColor { get; set; } = "#444";
}

public sealed class DefaultButtonTemplate : Button
{
    public DefaultButtonTemplate(DefaultButtonConfig? config = null)
    {
        var cfg = config ?? new DefaultButtonConfig();
        FontSize = cfg.FontSize ?? 16;
        MinWidth = cfg.Width ?? 160;
        MinHeight = cfg.Height ?? 40;
        HorizontalContentAlignment = HorizontalAlignment.Center;
        VerticalContentAlignment = VerticalAlignment.Center;
        Bind(BackgroundProperty, PaletteBinding.Create(this, "Background1"));
        Bind(ForegroundProperty, PaletteBinding.Create(this, "FontColor0"));

        Bind(ContentProperty, new Binding(nameof(ButtonWidgetViewModel.Text)));
        Bind(IsEnabledProperty, new Binding(nameof(ButtonWidgetViewModel.IsEnabled)));
        Bind(CommandProperty, new Binding(nameof(ButtonWidgetViewModel.Command)));
    }
}

public sealed class LargeButtonConfig : PresentationConfig
{
    public LargeButtonConfig() { FontSize = 20; Width = 300; Height = 60; }
}

public sealed class LargeButtonTemplate : Button
{
    public LargeButtonTemplate(LargeButtonConfig? config = null)
    {
        var cfg = config ?? new LargeButtonConfig();
        FontSize = cfg.FontSize ?? 20;
        MinWidth = cfg.Width ?? 300;
        MinHeight = cfg.Height ?? 60;
        HorizontalContentAlignment = HorizontalAlignment.Center;
        VerticalContentAlignment = VerticalAlignment.Center;
        Bind(BackgroundProperty, PaletteBinding.Create(this, "HighlightBackground"));
        Bind(ForegroundProperty, PaletteBinding.Create(this, "FontHighlightColor"));

        Bind(ContentProperty, new Binding(nameof(ButtonWidgetViewModel.Text)));
        Bind(IsEnabledProperty, new Binding(nameof(ButtonWidgetViewModel.IsEnabled)));
        Bind(CommandProperty, new Binding(nameof(ButtonWidgetViewModel.Command)));
    }
}
