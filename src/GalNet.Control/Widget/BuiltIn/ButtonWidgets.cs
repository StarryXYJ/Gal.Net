using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GalNet.Core.Widget;

namespace GalNet.Control.Widget.BuiltIn;

public sealed class DefaultButtonConfig
{
    public double FontSize { get; set; } = 16;
    public double Width { get; set; } = 160;
    public double Height { get; set; } = 40;
    public string BackgroundColor { get; set; } = "#444";
}

public sealed class DefaultButtonTemplate : Button, IButtonWidget
{
    public string Category => "Button";

    public event Action? Clicked;

    public DefaultButtonTemplate(DefaultButtonConfig? config = null)
    {
        var cfg = config ?? new DefaultButtonConfig();
        FontSize = cfg.FontSize;
        MinWidth = cfg.Width;
        MinHeight = cfg.Height;
        Background = Brush.Parse(cfg.BackgroundColor);
        Foreground = Brushes.White;

        Click += (_, _) => Clicked?.Invoke();
    }

    public void SetText(string text) => Content = text;
}

public sealed class LargeButtonConfig
{
    public double FontSize { get; set; } = 20;
    public double Width { get; set; } = 300;
    public double Height { get; set; } = 60;
}

public sealed class LargeButtonTemplate : Button, IButtonWidget
{
    public string Category => "Button";

    public event Action? Clicked;

    public LargeButtonTemplate(LargeButtonConfig? config = null)
    {
        var cfg = config ?? new LargeButtonConfig();
        FontSize = cfg.FontSize;
        MinWidth = cfg.Width;
        MinHeight = cfg.Height;
        Foreground = Brushes.White;

        Click += (_, _) => Clicked?.Invoke();
    }

    public void SetText(string text) => Content = text;
}
