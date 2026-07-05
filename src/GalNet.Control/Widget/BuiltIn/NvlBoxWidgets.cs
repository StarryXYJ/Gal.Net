using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GalNet.Core.Widget;

namespace GalNet.Control.Widget.BuiltIn;

public sealed class DefaultNvlConfig
{
    public double BackgroundOpacity { get; set; } = 0.75;
    public double FontSize { get; set; } = 15;
    public int MaxLines { get; set; } = 20;
}

public sealed class DefaultNvlTemplate : Border, INvlWidget
{
    public string Category => "NvlBox";

    private readonly TextBlock _textBlock;

    int INvlWidget.MaxLines
    {
        get => _textBlock.MaxLines;
        set => _textBlock.MaxLines = value;
    }

    public DefaultNvlTemplate(DefaultNvlConfig? config = null)
    {
        var cfg = config ?? new DefaultNvlConfig();

        _textBlock = new TextBlock
        {
            FontSize = cfg.FontSize,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.White,
            MaxLines = cfg.MaxLines,
            LineHeight = cfg.FontSize * 1.6,
            Margin = new Avalonia.Thickness(20),
        };

        Background = new SolidColorBrush(
            Color.FromArgb((byte)(cfg.BackgroundOpacity * 255), 0, 0, 0));
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        Child = _textBlock;
    }

    public void AppendText(string text, string? speaker = null)
    {
        var line = speaker != null ? $"{speaker}: {text}" : text;
        _textBlock.Text += (_textBlock.Text.Length > 0 ? "\n" : "") + (line ?? "");
    }

    public void Clear() => _textBlock.Text = "";
}
