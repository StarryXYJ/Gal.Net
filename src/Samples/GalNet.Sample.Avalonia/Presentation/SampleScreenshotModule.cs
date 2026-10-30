using GalNet.Avalonia.GameHost.Presentation;
using GalNet.Avalonia.GameView.Page;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Project.Contracts;
using GalNet.Runtime.Logging;

namespace GalNet.Sample.Avalonia.Presentation;

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
                    context.BatchId, context.ScopeCancellation, scope.CancellationToken, ScreenshotSchema.ValidateFileName);
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
