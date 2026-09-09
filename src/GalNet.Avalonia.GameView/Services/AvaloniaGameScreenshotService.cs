using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace GalNet.Avalonia.GameView.Services;

/// <summary>Default native Avalonia screenshot prompt with preview, destination and UI inclusion.</summary>
public sealed class AvaloniaGameScreenshotService : IGameScreenshotService
{
    public async Task CaptureAsync(GameScreenshotRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Owner is not Window owner) return;

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            SanitizeFileName(request.GameTitle));
        var fileName = $"Screenshot-{DateTime.Now:yyyyMMdd-HHmmss}.png";
        var preview = new Image { Stretch = global::Avalonia.Media.Stretch.Uniform, MaxHeight = 330 };
        var includeUi = new CheckBox { Content = "Include player UI" };
        var pathLabel = new TextBlock { Text = directory, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
        var nameBox = new TextBox { Text = fileName, PlaceholderText = "Screenshot.png" };
        var message = new TextBlock { Foreground = global::Avalonia.Media.Brushes.IndianRed, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
        var chooseDirectory = new Button { Content = "Choose folder…" };
        var save = new Button { Content = "Save", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };

        var layout = new Grid
        {
            Margin = new Thickness(20),
            RowDefinitions = new RowDefinitions("*,Auto,Auto,Auto,Auto"),
            RowSpacing = 10
        };
        var options = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children = { includeUi, chooseDirectory }
        };
        var folder = new StackPanel
        {
            Spacing = 4,
            Children = { new TextBlock { Text = "Folder" }, pathLabel }
        };
        var name = new StackPanel
        {
            Spacing = 4,
            Children = { new TextBlock { Text = "File name" }, nameBox, message }
        };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancel, save }
        };
        layout.Children.Add(preview);
        layout.Children.Add(options);
        layout.Children.Add(folder);
        layout.Children.Add(name);
        layout.Children.Add(actions);
        Grid.SetRow(options, 1);
        Grid.SetRow(folder, 2);
        Grid.SetRow(name, 3);
        Grid.SetRow(actions, 4);

        var dialog = new Window
        {
            Title = "Save screenshot",
            Width = 720,
            MinWidth = 500,
            Height = 560,
            MinHeight = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = layout
        };

        async Task RefreshPreviewAsync()
        {
            try
            {
                var bytes = await request.CapturePngAsync(includeUi.IsChecked == true);
                await using var stream = new MemoryStream(bytes);
                preview.Source = new Bitmap(stream);
                message.Text = string.Empty;
            }
            catch (Exception exception)
            {
                message.Text = $"Could not capture screenshot: {exception.Message}";
            }
        }

        includeUi.IsCheckedChanged += async (_, _) => await RefreshPreviewAsync();
        chooseDirectory.Click += async (_, _) =>
        {
            var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose screenshot folder",
                AllowMultiple = false
            });
            if (folders.Count == 0) return;
            directory = folders[0].Path.LocalPath;
            pathLabel.Text = directory;
        };
        cancel.Click += (_, _) => dialog.Close();
        save.Click += async (_, _) =>
        {
            try
            {
                var requestedName = nameBox.Text?.Trim();
                if (string.IsNullOrWhiteSpace(requestedName)) throw new InvalidOperationException("Enter a file name.");
                if (!requestedName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) requestedName += ".png";
                Directory.CreateDirectory(directory);
                var bytes = await request.CapturePngAsync(includeUi.IsChecked == true);
                await File.WriteAllBytesAsync(Path.Combine(directory, SanitizeFileName(requestedName)), bytes, cancellationToken);
                dialog.Close();
            }
            catch (Exception exception)
            {
                message.Text = $"Could not save screenshot: {exception.Message}";
            }
        };

        await RefreshPreviewAsync();
        await dialog.ShowDialog(owner);
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "GalNet" : sanitized;
    }
}
