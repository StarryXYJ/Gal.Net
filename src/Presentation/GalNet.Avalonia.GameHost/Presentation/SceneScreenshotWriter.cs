using Avalonia.Threading;
using GalNet.Assets;
using GalNet.Avalonia.GameView.Page;

namespace GalNet.Avalonia.GameHost.Presentation;

/// <summary>Captures a borrowed scene and publishes a new PNG without replacing existing files.</summary>
public static class SceneScreenshotWriter
{
    public static async Task<string> SaveAsync(GamePage page, string directory, string fileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        cancellationToken.ThrowIfCancellationRequested();
        FileSystemPaths.ValidateRelativeFile(fileName);
        if (fileName.Contains('/') || fileName.Any(character => char.IsControl(character) || "<>:\"|?*".Contains(character)) ||
            !fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A screenshot must use a single PNG file name.", nameof(fileName));
        if (!Path.IsPathFullyQualified(directory))
            throw new InvalidDataException("The host screenshot directory must be absolute.");
        directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        RejectLinks(directory);
        var target = Path.Combine(directory, fileName);
        if (File.Exists(target) || Directory.Exists(target))
            throw new IOException($"Screenshot already exists: '{target}'.");
        var png = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            page.EnsureLayout();
            if (page.SceneHost.Bounds.Width <= 0 || page.SceneHost.Bounds.Height <= 0)
                throw new InvalidOperationException("The game scene must be laid out before a screenshot executes.");
            return page.CaptureScenePng();
        });

        string? temporary = null;
        Exception? failure = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectLinks(directory);
            Directory.CreateDirectory(directory);
            temporary = Path.Combine(directory, ".galnet-screenshot-" + Guid.NewGuid().ToString("N") + ".tmp");
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
                await stream.WriteAsync(png, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            RejectLinks(directory);
            File.Move(temporary, target, overwrite: false);
            temporary = null;
        }
        catch (Exception exception) { failure = exception; }
        finally
        {
            try { if (temporary is not null) File.Delete(temporary); }
            catch (Exception cleanup) { failure = failure is null ? cleanup : new AggregateException(failure, cleanup); }
        }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return target;
    }

    private static void RejectLinks(string directory) =>
        FileSystemPaths.RequireNoLinks(directory, "The host screenshot directory cannot traverse links.");
}
