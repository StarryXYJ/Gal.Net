using GalNet.Assets;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Presentation.Abstractions.View;
using GalNet.Primitives.Builtins;
using GalNet.Rendering.Scene;
using GalNet.Runtime.Logging;
using GalNet.Sample.Avalonia.Presentation;

namespace GalNet.Sample.Avalonia.Services;

/// <summary>Sample composition and ownership of platform resources; no story execution logic.</summary>
internal sealed class SampleGamePresentation : IAsyncDisposable
{
    private SampleLayerFactory? _layers;
    private SkiaShaderEffectProgramResolver? _programs;
    private Task? _disposeTask;

    private SampleGamePresentation() { }

    public AvaloniaGamePresenters Presenters { get; private set; } = null!;
    public AvaloniaEffectRuntime Effects { get; private set; } = null!;
    public AvaloniaAudioPlaybackHost Audio { get; private set; } = null!;
    public AvaloniaVideoPresenter Video { get; private set; } = null!;
    public CompositeGameView View { get; private set; } = null!;

    public static async Task<SampleGamePresentation> CreateAsync(
        GamePageViewModel gameplay, GamePage page, SampleGameResourceScope resources,
        CancellationToken cancellationToken)
    {
        var presentation = new SampleGamePresentation();
        try
        {
            presentation._layers = new SampleLayerFactory(resources.PreloadedTextures);
            presentation.Presenters = new AvaloniaGamePresenters(gameplay, page, presentation._layers);
            presentation._programs = new SkiaShaderEffectProgramResolver(
                new AssetManagerShaderEffectProgramSource(resources.Assets));
            var preload = await presentation._programs.PreloadAsync(resources.EffectPrograms, cancellationToken);
            GameLog.Logger.Information(
                "Preloaded {UsableEffectProgramCount}/{EffectProgramCount} effect programs in {ElapsedMilliseconds} ms",
                preload.UsableProgramCount, preload.RequestedProgramCount, preload.Cache.TotalLoadTime.TotalMilliseconds);
            presentation.Effects = new AvaloniaEffectRuntime(gameplay, presentation._programs);
            presentation.Audio = new AvaloniaAudioPlaybackHost(resources.Assets);
            presentation.Video = new AvaloniaVideoPresenter(resources.Assets, gameplay);
            var presenters = presentation.Presenters;
            using var registration = new GameRegistration();
            registration.AddBuiltins(
                presenters.DialoguePresenter, presenters.LayerPresenter, presenters.AnimationPresenter,
                presentation.Effects, resources.Content.Gallery, presenters.ParticlePresenter,
                presenters.NvlPresenter, presentation.Audio, presentation.Video);
            presentation.View = registration.BuildView();
            return presentation;
        }
        catch (Exception error)
        {
            try { await presentation.DisposeAsync(); }
            catch (Exception cleanupError) { throw new AggregateException(error, cleanupError); }
            throw;
        }
    }

    public ValueTask DisposeAsync() => new(_disposeTask ??= DisposeCoreAsync());

    private async Task DisposeCoreAsync()
    {
        List<Exception>? errors = null;
        // Implementations borrow platform ports, so release the view first.
        await ReleaseAsync(() => View?.DisposeAsync() ?? ValueTask.CompletedTask);
        await ReleaseAsync(() => Video?.DisposeAsync() ?? ValueTask.CompletedTask);
        await ReleaseAsync(() => Audio?.DisposeAsync() ?? ValueTask.CompletedTask);
        await ReleaseAsync(() =>
        {
            if (Effects is not null) Effects.Dispose(); // owns the program resolver
            else _programs?.Dispose();
            return ValueTask.CompletedTask;
        });
        await ReleaseAsync(() => { Presenters?.Dispose(); return ValueTask.CompletedTask; });
        await ReleaseAsync(() => { _layers?.Dispose(); return ValueTask.CompletedTask; });
        if (errors is not null) throw new AggregateException(errors);

        async Task ReleaseAsync(Func<ValueTask> release)
        {
            try { await release(); }
            catch (Exception error) { (errors ??= []).Add(error); }
        }
    }
}
