using GalNet.Avalonia.GameHost.Presentation;
using GalNet.Avalonia.GameView.Page;
using GalNet.Core.Primitives;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Project.Contracts;
using GalNet.Runtime.Logging;

namespace GalNet.Sample.Player.Presentation;

/// <summary>The shared Sample implementation borrows GamePage; its platform supplies the output directory.</summary>
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
        var token = scope.Token;
        token.ThrowIfCancellationRequested();
        var name = fileName ?? $"Screenshot-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.png";
        ScreenshotSchema.ValidateFileName(name);
        SavedPath = await SceneScreenshotWriter.SaveAsync(page, outputDirectory(), name, token).ConfigureAwait(false);
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
