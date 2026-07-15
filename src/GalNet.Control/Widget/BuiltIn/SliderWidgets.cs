using Avalonia.Controls;
using GalNet.Control.Abstraction.UI;

namespace GalNet.Control.Widget.BuiltIn;

public sealed class DefaultSliderConfig : PresentationConfig
{
    public double Minimum { get; set; } = 0;
    public double Maximum { get; set; } = 100;
    public double Value { get; set; } = 50;
    public double Step { get; set; } = 1;
    public string? Label { get; set; }
    public bool ShowValue { get; set; } = true;
}

/// <summary>Slider View; value range and change event live in SliderWidgetViewModel.</summary>
public partial class DefaultSliderTemplate : UserControl
{
    public DefaultSliderTemplate() : this(null) { }
    public DefaultSliderTemplate(DefaultSliderConfig? config = null) => InitializeComponent();
}
