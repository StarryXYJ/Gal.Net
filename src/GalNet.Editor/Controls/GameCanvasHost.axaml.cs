using Avalonia;
using Avalonia.Controls;

namespace GalNet.Editor.Controls;

/// <summary>A fixed design canvas centered in available space with a single letterbox direction.</summary>
public partial class GameCanvasHost : UserControl
{
    public static readonly StyledProperty<int> DesignWidthProperty = AvaloniaProperty.Register<GameCanvasHost, int>(nameof(DesignWidth), 1920);
    public static readonly StyledProperty<int> DesignHeightProperty = AvaloniaProperty.Register<GameCanvasHost, int>(nameof(DesignHeight), 1080);
    public int DesignWidth { get => GetValue(DesignWidthProperty); set => SetValue(DesignWidthProperty, value); }
    public int DesignHeight { get => GetValue(DesignHeightProperty); set => SetValue(DesignHeightProperty, value); }
    public Avalonia.Controls.Control? GameContent { get => CanvasContent.Content as Avalonia.Controls.Control; set => CanvasContent.Content = value; }
    public GameCanvasHost() => InitializeComponent();
}
