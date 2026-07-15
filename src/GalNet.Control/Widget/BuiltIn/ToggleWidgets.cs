using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using GalNet.Control.Widget;
using GalNet.Control.Abstraction.UI;

namespace GalNet.Control.Widget.BuiltIn;

public sealed class DefaultToggleConfig : PresentationConfig
{
    public string Label { get; set; } = "";
    public bool IsChecked { get; set; }
}

/// <summary>Compact command-bar presentation for the standard toggle VM.</summary>
public sealed class CommandToggleTemplate : ToggleButton
{
    public CommandToggleTemplate(DefaultToggleConfig? config = null)
    {
        HorizontalContentAlignment = HorizontalAlignment.Center;
        VerticalContentAlignment = VerticalAlignment.Center;
        Bind(ContentProperty, new Binding(nameof(ToggleWidgetViewModel.Label)));
        Bind(IsCheckedProperty, new Binding(nameof(ToggleWidgetViewModel.IsChecked)) { Mode = BindingMode.TwoWay });
    }
}

/// <summary>Toggle View; checked state and callbacks live in ToggleWidgetViewModel.</summary>
public partial class DefaultToggleTemplate : UserControl
{
    public DefaultToggleTemplate() : this(null) { }
    public DefaultToggleTemplate(DefaultToggleConfig? config = null) => InitializeComponent();
}
