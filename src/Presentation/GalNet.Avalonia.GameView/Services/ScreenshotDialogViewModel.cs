using GalNet.Avalonia.GameView.MarkupExtensions;
using Irihi.Avalonia.Shared.Contracts;

namespace GalNet.Avalonia.GameView.Services;

public sealed class ScreenshotDialogViewModel : IDialogContext
{
    private readonly Func<bool, Task<byte[]>> _capture;
    private readonly Func<string, string, bool, Task<string?>> _save;
    private readonly Dictionary<bool, Task<byte[]>> _previews = [];

    public ScreenshotDialogViewModel(
        string directoryPath,
        string fileName,
        Func<bool, Task<byte[]>> capture,
        Func<string, string, bool, Task<string?>> save)
    {
        DirectoryPath = directoryPath;
        FileName = fileName;
        _capture = capture;
        _save = save;
    }

    public string DirectoryPath { get; set; }
    public string FileName { get; set; }
    public bool IncludeUi { get; set; }
    public string? Error { get; private set; }
    public event EventHandler<object?>? RequestClose;

    public Task<byte[]> CapturePreviewAsync()
    {
        var includeUi = IncludeUi;
        if (_previews.TryGetValue(includeUi, out var cached) && !cached.IsFaulted && !cached.IsCanceled) return cached;
        return _previews[includeUi] = _capture(includeUi);
    }

    public async Task SaveAsync()
    {
        Error = await _save(DirectoryPath, FileName, IncludeUi);
        if (Error is null) RequestClose?.Invoke(this, true);
    }

    public void Cancel() => RequestClose?.Invoke(this, false);
    public void Close() => Cancel();
    internal static string Text(string key, string fallback) => PlayerTextExtension.Resolve?.Invoke(key)?.ToString() ?? fallback;
}
