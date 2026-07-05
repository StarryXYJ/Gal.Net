using Avalonia.Controls;
using Avalonia.Media;
using GalNet.Core.Widget;

namespace GalNet.Control.Widget.BuiltIn;

public sealed class DefaultTitleButtonConfig
{
    public double FontSize { get; set; } = 20;
    public double Width { get; set; } = 260;
    public double Height { get; set; } = 50;
}

/// <summary>
/// Title screen menu button.
/// </summary>
public sealed class DefaultTitleButtonTemplate : Button, IButtonWidget
{
    public string Category => "TitleButton";

    public event Action? Clicked;

    public DefaultTitleButtonTemplate(DefaultTitleButtonConfig? config = null)
    {
        var cfg = config ?? new DefaultTitleButtonConfig();
        FontSize = cfg.FontSize;
        MinWidth = cfg.Width;
        MinHeight = cfg.Height;
        Foreground = Brushes.White;

        Click += (_, _) => Clicked?.Invoke();
    }

    public void SetText(string text) => Content = text;
}
