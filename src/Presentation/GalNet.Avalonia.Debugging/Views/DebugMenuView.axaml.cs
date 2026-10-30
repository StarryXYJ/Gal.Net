using Avalonia.Controls;

namespace GalNet.Avalonia.Debugging.Views;

public partial class DebugMenuView : Menu
{
    protected override Type StyleKeyOverride => typeof(Menu);

    public DebugMenuView() => InitializeComponent();
}
