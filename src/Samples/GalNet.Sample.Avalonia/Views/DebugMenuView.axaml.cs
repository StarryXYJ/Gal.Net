using Avalonia.Controls;

namespace GalNet.Sample.Avalonia.Views;

public partial class DebugMenuView : Menu
{
    protected override Type StyleKeyOverride => typeof(Menu);

    public DebugMenuView() => InitializeComponent();
}
