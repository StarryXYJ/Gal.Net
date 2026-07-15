using Avalonia.Controls;
using GalNet.Control.Abstraction.UI;

namespace GalNet.Control.Widget.BuiltIn;

public sealed class DefaultChoiceConfig : PresentationConfig
{
    public double ButtonSpacing { get; set; } = 8;
    public DefaultChoiceConfig() => FontSize = 16;
    public double ButtonWidth { get; set; } = 200;
    public double ButtonHeight { get; set; } = 40;
}

/// <summary>Pure choice View. Commands and option lifetime belong to <c>ChoicePanelWidgetViewModel</c>.</summary>
public partial class DefaultChoiceTemplate : UserControl
{
    public DefaultChoiceTemplate() : this(null) { }
    public DefaultChoiceTemplate(DefaultChoiceConfig? config = null) => InitializeComponent();
}

public sealed class HorizontalChoiceConfig : PresentationConfig
{
    public double ButtonSpacing { get; set; } = 12;
    public HorizontalChoiceConfig() => FontSize = 16;
    public double ButtonWidth { get; set; } = 160;
    public double ButtonHeight { get; set; } = 50;
}

public partial class HorizontalChoiceTemplate : UserControl
{
    public HorizontalChoiceTemplate() : this(null) { }
    public HorizontalChoiceTemplate(HorizontalChoiceConfig? config = null) => InitializeComponent();
}
