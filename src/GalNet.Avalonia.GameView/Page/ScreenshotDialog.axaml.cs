using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using GalNet.Avalonia.GameView.Services;

namespace GalNet.Avalonia.GameView.Page;

public partial class ScreenshotDialog : UserControl
{
    private ScreenshotDialogViewModel? _viewModel;
    private Bitmap? _previewBitmap;

    public ScreenshotDialog() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not ScreenshotDialogViewModel viewModel) return;

        _viewModel = viewModel;
        DirectoryBox.Text = viewModel.DirectoryPath;
        FileNameBox.Text = viewModel.FileName;
        IncludeUiBox.IsChecked = viewModel.IncludeUi;

        BrowseButton.Click += OnBrowseClicked;
        CancelButton.Click += (_, _) => viewModel.Cancel();
        SaveButton.Click += OnSaveClicked;
        IncludeUiBox.IsCheckedChanged += OnIncludeUiChanged;
        _ = RefreshPreviewAsync(viewModel);
    }

    private async void OnBrowseClicked(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose screenshot folder",
            AllowMultiple = false
        });
        if (folders.Count > 0)
            DirectoryBox.Text = folders[0].TryGetLocalPath() ?? DirectoryBox.Text;
    }

    private async void OnIncludeUiChanged(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.IncludeUi = IncludeUiBox.IsChecked == true;
            await RefreshPreviewAsync(_viewModel);
        }
    }

    private async void OnSaveClicked(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel is null) return;

        _viewModel.DirectoryPath = DirectoryBox.Text?.Trim() ?? string.Empty;
        _viewModel.FileName = FileNameBox.Text?.Trim() ?? string.Empty;
        _viewModel.IncludeUi = IncludeUiBox.IsChecked == true;
        await _viewModel.SaveAsync();
        ErrorBlock.Text = _viewModel.Error;
    }

    private async Task RefreshPreviewAsync(ScreenshotDialogViewModel viewModel)
    {
        try
        {
            var bytes = await viewModel.CapturePreviewAsync();
            await using var stream = new MemoryStream(bytes);
            var bitmap = new Bitmap(stream);
            var previous = _previewBitmap;
            _previewBitmap = bitmap;
            Preview.Source = bitmap;
            previous?.Dispose();
            ErrorBlock.Text = string.Empty;
        }
        catch (Exception exception)
        {
            ErrorBlock.Text = $"Could not capture screenshot: {exception.Message}";
        }
    }
}
