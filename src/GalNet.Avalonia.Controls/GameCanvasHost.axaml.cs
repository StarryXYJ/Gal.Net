using Avalonia;
using Avalonia.Controls;

namespace GalNet.Game.Controls;

/// <summary>
/// Hosts a fixed design canvas, centered and uniformly scaled inside the available bounds.
/// Scaling is deliberately a host concern; game content remains resolution-agnostic.
/// </summary>
public partial class GameCanvasHost : UserControl
{
    public static readonly StyledProperty<int> DesignWidthProperty =
        AvaloniaProperty.Register<GameCanvasHost, int>(nameof(DesignWidth), 1920);

    public static readonly StyledProperty<int> DesignHeightProperty =
        AvaloniaProperty.Register<GameCanvasHost, int>(nameof(DesignHeight), 1080);

    public int DesignWidth
    {
        get => GetValue(DesignWidthProperty);
        set => SetValue(DesignWidthProperty, value);
    }

    public int DesignHeight
    {
        get => GetValue(DesignHeightProperty);
        set => SetValue(DesignHeightProperty, value);
    }

    public Control? GameContent
    {
        get => CanvasContent.Content as Control;
        set => CanvasContent.Content = value;
    }

    public GameCanvasHost() => InitializeComponent();
}
