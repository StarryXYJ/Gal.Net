using Avalonia.Controls;
namespace GalNet.Control.Screen.SaveLoad;
public partial class SaveLoadView : UserControl
{
    public SaveLoadView() => InitializeComponent();
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is SaveLoadViewModel viewModel)
            _ = viewModel.RefreshAsync();
    }
}
