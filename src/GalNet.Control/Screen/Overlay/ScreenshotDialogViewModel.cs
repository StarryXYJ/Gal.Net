using Irihi.Avalonia.Shared.Contracts;

namespace GalNet.Control.Screen.Overlay;

public sealed class ScreenshotDialogViewModel : IDialogContext
{
    private readonly Func<string, bool, Task<string?>> _save;
    public string DirectoryPath { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
    public bool IncludeUi { get; set; }
    public string? Error { get; private set; }
    public event EventHandler<object?>? RequestClose;
    public ScreenshotDialogViewModel(Func<string, bool, Task<string?>> save) => _save = save;
    public async Task SaveAsync()
    {
        Error = await _save(DirectoryPath, IncludeUi);
        if (Error is null) RequestClose?.Invoke(this, true);
    }
    public void Cancel() => RequestClose?.Invoke(this, false);
    public void Close() => Cancel();
}
