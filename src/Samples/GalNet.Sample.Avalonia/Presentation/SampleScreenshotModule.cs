using Avalonia.Threading;
using GalNet.Avalonia.GameView.Page;
using GalNet.Core.Primitives;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Project.Contracts;
using GalNet.Runtime.Logging;

namespace GalNet.Sample.Avalonia.Presentation;

/// <summary>A single-platform implementation may borrow the concrete GamePage directly.</summary>
public sealed class SampleScreenshotModule : PrimitiveModule
{
    private readonly PrimitiveTaskScope _scope;
    public SampleScreenshotModule(GamePage page, Func<string> outputDirectory) : this(page, outputDirectory, new PrimitiveTaskScope()) { }
    private SampleScreenshotModule(GamePage page, Func<string> outputDirectory, PrimitiveTaskScope scope) : base(
    registry => registry.Register<ScreenshotOptions>(ScreenshotSchema.TypeId, (context, options) =>
            {
                scope.ThrowIfDisposed();
                return new ScreenshotPrimitiveInstance(scope, page, outputDirectory,
                    options.FileName,
                    context.BatchId, context.ScopeCancellation, scope.CancellationToken);
            })
        )
        => _scope = scope;

    public override void Dispose()
    {
        _scope.Dispose();
        base.Dispose();
    }

    public override async ValueTask DisposeAsync()
    {
        try { await _scope.DisposeAsync().ConfigureAwait(false); }
        catch (Exception exception) { GameLog.Logger.Error(exception, "Screenshot presentation failed while draining the module."); }
        finally { await base.DisposeAsync().ConfigureAwait(false); }
    }
}

public sealed class ScreenshotPrimitiveInstance(PrimitiveTaskScope? taskScope, GamePage page,
    Func<string> outputDirectory, string? fileName, string? batchId, CancellationToken cancellationToken,
    CancellationToken lifetimeCancellation = default) : AsyncPrimitiveInstance(batchId, blocking: true, cancellationToken)
{
    public ScreenshotPrimitiveInstance(GamePage page, Func<string> outputDirectory, string? fileName,
        string? batchId, CancellationToken cancellationToken, CancellationToken lifetimeCancellation = default)
        : this(null, page, outputDirectory, fileName, batchId, cancellationToken, lifetimeCancellation) { }
    public string? SavedPath { get; private set; }

    protected override Task ExecuteAsync(CancellationToken token)
    {
        try { return taskScope is null ? CaptureAsync(token) : taskScope.Run(_ => CaptureAsync(token)); }
        catch (ObjectDisposedException) when (taskScope is not null)
        {
            return Task.FromException(new OperationCanceledException(taskScope.CancellationToken));
        }
    }
    protected override void OnCatchUpDispatch() => CompleteWithoutExecution();
    protected override bool HandleExecutionFailure(Exception exception)
    {
        if (exception is not OperationCanceledException) GameLog.Logger.Error(exception, "Screenshot entry failed.");
        return false;
    }

    private async Task CaptureAsync(CancellationToken cancellation)
    {
        using var scope = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetimeCancellation);
        string? temporary = null;
        Exception? failure = null;
        var token = scope.Token;
        try
        {
            token.ThrowIfCancellationRequested();
            var name = fileName ?? $"Screenshot-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.png";
            ScreenshotSchema.ValidateFileName(name);
            var directory = outputDirectory();
            if (!Path.IsPathFullyQualified(directory)) throw new InvalidDataException("The host screenshot directory must be absolute.");
            directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            RejectLinks(directory);
            var target = Path.Combine(directory, name);
            if (File.Exists(target) || Directory.Exists(target)) throw new IOException($"Screenshot already exists: '{target}'.");
            var png = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                token.ThrowIfCancellationRequested();
                page.EnsureLayout();
                if (page.SceneHost.Bounds.Width <= 0 || page.SceneHost.Bounds.Height <= 0)
                    throw new InvalidOperationException("The game scene must be laid out before a screenshot entry executes.");
                return page.CaptureScenePng();
            });
            token.ThrowIfCancellationRequested();
            RejectLinks(directory);
            Directory.CreateDirectory(directory);
            temporary = Path.Combine(directory, ".galnet-screenshot-" + Guid.NewGuid().ToString("N") + ".tmp");
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
                await stream.WriteAsync(png, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            RejectLinks(directory);
            File.Move(temporary, target, overwrite: false);
            temporary = null;
            SavedPath = target;
        }
        catch (Exception exception) { failure = exception; }
        finally
        {
            try { if (temporary is not null) File.Delete(temporary); }
            catch (Exception cleanup) { failure = failure is null ? cleanup : new AggregateException(failure, cleanup); }
        }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void RejectLinks(string directory)
    {
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("The host screenshot directory cannot traverse links.");
    }
}

public static class SampleScreenshotOutput
{
    public static string GetHostDirectory(string gameTitle)
    {
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrWhiteSpace(pictures) || !Path.IsPathFullyQualified(pictures))
            throw new InvalidOperationException("The host could not resolve its Pictures directory.");
        var title = new string(gameTitle.Select(character => char.IsControl(character) || "<>:\"/\\|?*".Contains(character) ? '_' : character).ToArray()).Trim(' ', '.');
        return Path.Combine(pictures, string.IsNullOrWhiteSpace(title) ? "GalNet" : title);
    }
}
