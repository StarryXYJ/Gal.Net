using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GalNet.Core.Widget;
using GalNet.Control.Abstraction.UI;
using GalNet.Control.Widget;
using Avalonia.Data;

namespace GalNet.Control.Widget.BuiltIn;

public sealed class DefaultNvlConfig : PresentationConfig
{
    public double BackgroundOpacity { get; set; } = 0.75;
    public DefaultNvlConfig() => FontSize = 15;
    public int MaxLines { get; set; } = 20;
}

public sealed class DefaultNvlTemplate : Border
{
    public string Category => "NvlBox";

    public DefaultNvlTemplate(DefaultNvlConfig? config = null)
    {
        var cfg = config ?? new DefaultNvlConfig();

        var textBlock = new TextBlock
        {
            FontSize = cfg.FontSize ?? 15,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = cfg.MaxLines,
            LineHeight = (cfg.FontSize ?? 15) * 1.6,
            Margin = new Avalonia.Thickness(20),
        };

        Bind(BackgroundProperty, PaletteBinding.Create(this, "Background1"));
        textBlock.Bind(TextBlock.TextProperty, new Binding(nameof(NvlWidgetViewModel.DisplayText)));
        textBlock.Bind(TextBlock.ForegroundProperty, PaletteBinding.Create(textBlock, "FontColor0"));
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        Child = textBlock;
    }
}
