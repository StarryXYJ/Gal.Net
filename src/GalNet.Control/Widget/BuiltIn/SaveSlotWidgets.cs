using Avalonia.Controls;
using GalNet.Control.Abstraction.UI;

namespace GalNet.Control.Widget.BuiltIn;

public sealed class DefaultSlotConfig : PresentationConfig
{
    public int SlotIndex { get; set; }
    public DefaultSlotConfig() { Width = 180; Height = 120; }
}

/// <summary>Save-slot View; slot data and selection event live in SaveSlotWidgetViewModel.</summary>
public partial class DefaultSlotTemplate : UserControl
{
    public DefaultSlotTemplate() : this(null) { }
    public DefaultSlotTemplate(DefaultSlotConfig? config = null) => InitializeComponent();
}
