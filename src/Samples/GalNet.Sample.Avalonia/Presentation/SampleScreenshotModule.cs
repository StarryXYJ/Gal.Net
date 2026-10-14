using System.Collections.Concurrent;
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
    private readonly Lifetime _lifetime;
    private Task? _disposeTask;
    public SampleScreenshotModule(GamePage page, Func<string> outputDirectory) : this(page, outputDirectory, new Lifetime()) { }
    private SampleScreenshotModule(GamePage page, Func<string> outputDirectory, Lifetime lifetime) : base(
    [
            PrimitiveImplementation.For<ScreenshotOptions>(ScreenshotSchema.TypeId, (context, options) =>
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref lifetime.Cancelled) != 0, lifetime);

                var instance = new ScreenshotPrimitiveInstance(page, outputDirectory,
                    options.FileName,
                    context.BatchId, context.ScopeCancellation, lifetime.Cancellation.Token);
                lifetime.Completions.Add(instance.PresentationCompletion);
                return instance;
            })
        ]
        )
        => _lifetime = lifetime;

    public override void Dispose()
    {
        if (Interlocked.Exchange(ref _lifetime.Cancelled, 1) == 0) _lifetime.Cancellation.Cancel();
        base.Dispose();
    }

    public override async ValueTask DisposeAsync()
    {
        await (_disposeTask ??= DisposeCoreAsync()).ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    private async Task DisposeCoreAsync()
    {
        Dispose();
        try { await Task.WhenAll(_lifetime.Completions).ConfigureAwait(false); }
        catch (OperationCanceledException) when (_lifetime.Cancellation.IsCancellationRequested) { }
        catch (Exception exception) { GameLog.Logger.Error(exception, "Screenshot presentation failed while draining the module."); }
        finally { _lifetime.Cancellation.Dispose(); }
    }

    private sealed class Lifetime
    {
        public CancellationTokenSource Cancellation { get; } = new();
        public int Cancelled;
        public ConcurrentBag<Task> Completions { get; } = [];
    }
}

public sealed class ScreenshotPrimitiveInstance(GamePage page, Func<string> outputDirectory, string? fileName,
    string? batchId, CancellationToken cancellationToken, CancellationToken lifetimeCancellation = default) : PrimitiveInstance(batchId)
{
    private readonly CancellationTokenSource _scope = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetimeCancellation);
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override bool IsBlocking => true;
    public override bool IsSkippable => false;
    public override Task PresentationCompletion => _completion.Task;
    public string? SavedPath { get; private set; }

    protected override void OnDispatch() => _ = CaptureAsync();
    protected override void OnQuickDispatch() => OnDispatch();
    protected override void OnCatchUpDispatch() { _scope.Dispose(); _completion.SetResult(); TryComplete(); }

    private async Task CaptureAsync()
    {
        string? temporary = null;
        Exception? failure = null;
        var token = _scope.Token;
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
            _scope.Dispose();
            if (failure is OperationCanceledException) _completion.TrySetCanceled(token);
            else if (failure is not null)
            {
                GameLog.Logger.Error(failure, "Screenshot entry failed.");
                _completion.TrySetException(failure);
            }
            else _completion.TrySetResult();
            TryComplete();
        }
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
