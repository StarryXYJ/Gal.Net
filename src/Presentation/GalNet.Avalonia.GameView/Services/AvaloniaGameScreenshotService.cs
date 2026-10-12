using Avalonia.Controls;
using GalNet.Avalonia.GameView.Composition;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Page;
using Microsoft.Extensions.DependencyInjection;
using Ursa.Controls;

namespace GalNet.Avalonia.GameView.Services;

/// <summary>Default Ursa overlay prompt with preview, destination and UI inclusion.</summary>
public sealed class AvaloniaGameScreenshotService(IGameLayoutTheme theme, IPageViewRegistry views, IServiceProvider services) : IGameScreenshotService
{
    public async Task CaptureAsync(GameScreenshotRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Owner is null) return;

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            SanitizeFileName(request.GameTitle));
        var fileName = $"Screenshot-{DateTime.Now:yyyyMMdd-HHmmss}.png";
        var viewModel = new ScreenshotDialogViewModel(
            directory,
            fileName,
            request.CapturePngAsync,
            async (targetDirectory, targetFileName, includeUi) =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(targetFileName))
                        return "Enter a file name.";

                    if (!targetFileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                        targetFileName += ".png";

                    Directory.CreateDirectory(targetDirectory);
                    var bytes = await request.CapturePngAsync(includeUi);
                    var path = Path.Combine(targetDirectory, SanitizeFileName(targetFileName));
                    await File.WriteAllBytesAsync(path, bytes, cancellationToken);
                    return null;
                }
                catch (Exception exception)
                {
                    return $"Could not save screenshot: {exception.Message}";
                }
            });

        using var dialog = (ScreenshotDialog)ActivatorUtilities.CreateInstance(services,
            views.GetViewType(typeof(ScreenshotDialogViewModel)));
        dialog.Styles.Add(theme.CreateStyles());
        await OverlayDialog.ShowCustomAsync<bool>(dialog, viewModel, null, null, cancellationToken);
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "GalNet" : sanitized;
    }
}
