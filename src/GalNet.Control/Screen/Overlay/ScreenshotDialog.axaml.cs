using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace GalNet.Control.Screen.Overlay;
public partial class ScreenshotDialog : UserControl
{
    public ScreenshotDialog() { InitializeComponent(); }
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not ScreenshotDialogViewModel vm) return;
        DirectoryBox.Text = vm.DirectoryPath; IncludeUiBox.IsChecked = vm.IncludeUi;
        BrowseButton.Click += async (_, _) =>
        {
            var top = TopLevel.GetTopLevel(this); if (top?.StorageProvider is null) return;
            var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Select screenshot directory", AllowMultiple = false });
            if (folders.Count > 0) DirectoryBox.Text = folders[0].TryGetLocalPath() ?? DirectoryBox.Text;
        };
        CancelButton.Click += (_, _) => vm.Cancel();
        SaveButton.Click += async (_, _) =>
        {
            vm.DirectoryPath = DirectoryBox.Text ?? ""; vm.IncludeUi = IncludeUiBox.IsChecked == true;
            await vm.SaveAsync(); ErrorBlock.Text = vm.Error;
        };
    }
}
