using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using GalNet.Avalonia.GameView.Services;

namespace GalNet.Avalonia.GameView.Page;

/// <summary>Shared screenshot behavior. Layouts supply the named controls and initialize after loading XAML.</summary>
public abstract class ScreenshotDialog : UserControl, IDisposable
{
    private ScreenshotDialogViewModel? _viewModel;
    private Bitmap? _previewBitmap;
    private int _previewVersion;

    private Image Preview = null!;
    private TextBox DirectoryBox = null!;
    private TextBox FileNameBox = null!;
    private CheckBox IncludeUiBox = null!;
    private Button BrowseButton = null!;
    private Button CancelButton = null!;
    private Button SaveButton = null!;
    private TextBlock ErrorBlock = null!;
    private bool _initialized;
    private TopLevel? _owner;

    protected override void OnAttachedToVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _owner = TopLevel.GetTopLevel(this);
        if (_owner is not null) _owner.SizeChanged += OnOwnerSizeChanged;
        UpdateSizing();
    }

    protected override void OnDetachedFromVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (_owner is not null) _owner.SizeChanged -= OnOwnerSizeChanged;
        _owner = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnOwnerSizeChanged(object? sender, SizeChangedEventArgs e) => UpdateSizing();

    private void UpdateSizing()
    {
        if (!_initialized) return;
        if (_viewModel is { CanChooseDestination: false } && _owner is not null)
        {
            MaxWidth = Math.Min(480, Math.Max(1, _owner.ClientSize.Width - 32));
            MaxHeight = Math.Max(1, _owner.ClientSize.Height - 24);
            Preview.Height = Math.Clamp(_owner.ClientSize.Height - 270, 48, 160);
        }
        else
        {
            MaxWidth = MaxHeight = double.PositiveInfinity;
            Preview.Height = 284;
        }
    }

    protected void InitializeDialog()
    {
        if (_initialized) throw new InvalidOperationException("Screenshot dialog behavior is already initialized.");
        Preview = Require<Image>(nameof(Preview));
        DirectoryBox = Require<TextBox>(nameof(DirectoryBox));
        FileNameBox = Require<TextBox>(nameof(FileNameBox));
        IncludeUiBox = Require<CheckBox>(nameof(IncludeUiBox));
        BrowseButton = Require<Button>(nameof(BrowseButton));
        CancelButton = Require<Button>(nameof(CancelButton));
        SaveButton = Require<Button>(nameof(SaveButton));
        ErrorBlock = Require<TextBlock>(nameof(ErrorBlock));
        _initialized = true;
        OnDataContextChanged(EventArgs.Empty);
    }

    private T Require<T>(string name) where T : Control
    {
        var message = $"Screenshot dialog layout requires '{name}' ({typeof(T).Name}) after loading XAML.";
        try
        {
            return this.FindControl<T>(name) ?? throw new InvalidOperationException(message);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException(message, exception);
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (!_initialized) return;
        DetachHandlers();
        _viewModel = null;
        _previewVersion++;
        if (DataContext is not ScreenshotDialogViewModel viewModel) return;

        _viewModel = viewModel;
        UpdateSizing();
        DirectoryBox.Text = viewModel.DirectoryPath;
        FileNameBox.Text = viewModel.FileName;
        IncludeUiBox.IsChecked = viewModel.IncludeUi;

        BrowseButton.Click += OnBrowseClicked;
        CancelButton.Click += OnCancelClicked;
        SaveButton.Click += OnSaveClicked;
        IncludeUiBox.IsCheckedChanged += OnIncludeUiChanged;
        _ = RefreshPreviewAsync(viewModel);
    }

    public void Dispose()
    {
        if (!_initialized) return;
        DetachHandlers();
        if (_owner is not null) _owner.SizeChanged -= OnOwnerSizeChanged;
        _owner = null;
        _viewModel = null;
        _previewVersion++;
        Preview.Source = null;
        _previewBitmap?.Dispose();
        _previewBitmap = null;
        GC.SuppressFinalize(this);
    }

    private void DetachHandlers()
    {
        BrowseButton.Click -= OnBrowseClicked;
        CancelButton.Click -= OnCancelClicked;
        SaveButton.Click -= OnSaveClicked;
        IncludeUiBox.IsCheckedChanged -= OnIncludeUiChanged;
    }

    private void OnCancelClicked(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => _viewModel?.Cancel();

    private async void OnBrowseClicked(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var viewModel = _viewModel;
        if (viewModel is null) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = ScreenshotDialogViewModel.Text("Screenshot.ChooseFolder", "Choose folder…"),
            AllowMultiple = false
        });
        if (ReferenceEquals(_viewModel, viewModel) && folders.Count > 0)
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
        var viewModel = _viewModel;
        if (viewModel is null) return;

        if (viewModel.CanChooseDestination)
        {
            viewModel.DirectoryPath = DirectoryBox.Text?.Trim() ?? string.Empty;
            viewModel.FileName = FileNameBox.Text?.Trim() ?? string.Empty;
        }
        viewModel.IncludeUi = IncludeUiBox.IsChecked == true;
        SaveButton.IsEnabled = false;
        try
        {
            await viewModel.SaveAsync();
            if (ReferenceEquals(_viewModel, viewModel)) ErrorBlock.Text = viewModel.Error;
        }
        finally { SaveButton.IsEnabled = true; }
    }

    private async Task RefreshPreviewAsync(ScreenshotDialogViewModel viewModel)
    {
        var version = ++_previewVersion;
        try
        {
            var bytes = await viewModel.CapturePreviewAsync();
            if (!ReferenceEquals(_viewModel, viewModel) || version != _previewVersion) return;
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
            if (!ReferenceEquals(_viewModel, viewModel) || version != _previewVersion) return;
            ErrorBlock.Text = $"{ScreenshotDialogViewModel.Text("Screenshot.CaptureFailed", "Could not capture screenshot.")} {exception.Message}";
        }
    }
}
