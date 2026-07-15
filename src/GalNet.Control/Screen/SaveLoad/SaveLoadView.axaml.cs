using Avalonia.Controls;
namespace GalNet.Control.Views;
public partial class SaveLoadView : UserControl
{
    public SaveLoadView() => InitializeComponent();
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ViewModels.SaveLoadViewModel viewModel)
            _ = viewModel.RefreshAsync();
    }
}
