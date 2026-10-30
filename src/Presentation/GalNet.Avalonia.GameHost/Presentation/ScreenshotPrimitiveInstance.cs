using GalNet.Avalonia.GameView.Page;
using GalNet.Core.Primitives;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Runtime.Logging;

namespace GalNet.Avalonia.GameHost.Presentation;

/// <summary>Scene capture dispatch and cancellation, independent of a project's entry schema.</summary>
public sealed class ScreenshotPrimitiveInstance(PrimitiveTaskScope? taskScope, GamePage page,
    Func<string> outputDirectory, string? fileName, string? batchId, CancellationToken cancellationToken,
    CancellationToken lifetimeCancellation = default, Action<string>? validateFileName = null)
    : AsyncPrimitiveInstance(batchId, blocking: true, cancellationToken)
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
        validateFileName?.Invoke(name);
        SavedPath = await SceneScreenshotWriter.SaveAsync(page, outputDirectory(), name, token).ConfigureAwait(false);
    }
}
