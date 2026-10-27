using GalNet.Assets;
using GalNet.Assets.Variants;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Core.Assets;
using GalNet.Contracts.Builtins.Audio;
using GalNet.Core.Execution;
using GalNet.Core.Runtime;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Contracts.Builtins.Variants;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;
using GalNet.Primitives.Builtins;
using GalNet.Rendering.Scene;
using GalNet.Runtime.Logging;
using LibVLCSharp.Shared;

namespace GalNet.Avalonia.GameHost;

/// <summary>Default Avalonia composition and ownership of platform resources; no story execution logic.</summary>
public sealed class AvaloniaGamePresentation : IAsyncDisposable, ICatchUpPresentation
{
    private IGamePageLayerFactory? _layers;
    private SkiaShaderEffectProgramResolver? _programs;
    private Task? _disposeTask;
    private readonly object _disposeGate = new();
    private GamePageViewModel _gameplay = null!;

    private AvaloniaGamePresentation() { }

    public AvaloniaGamePresenters Presenters { get; private set; } = null!;
    public AvaloniaEffectRuntime Effects { get; private set; } = null!;
    public IAudioPlaybackHost Audio { get; private set; } = null!;
    public IVideoPresenter Video { get; private set; } = null!;
    public CompositeGameView View { get; private set; } = null!;
    public BuiltinAudioModule AudioModule { get; private set; } = null!;
    public StateRestoreSession Restorer { get; private set; } = null!;
    public ImageVariantsCatalog ImageVariants { get; private set; } = ImageVariantsCatalog.Empty;

    public static async Task<AvaloniaGamePresentation> CreateAsync(
        GamePageViewModel gameplay, GamePage page, IAssetManager assets,
        IReadOnlyDictionary<string, AssetHandle<SceneTexture>> textures,
        IReadOnlyList<EffectProgramResource> effectPrograms, IAssetFileMaterializer files,
        Action<GameRegistration, AvaloniaGamePresentation> configure,
        IEnumerable<string> requiredInstructions,
        AvaloniaGamePresentationOptions? options = null, LibVLC? sharedLibVlc = null,
        AudioConfiguration? audioConfiguration = null, CancellationToken cancellationToken = default)
    {
        options ??= new AvaloniaGamePresentationOptions();
        var presentation = new AvaloniaGamePresentation();
        presentation._gameplay = gameplay;
        try
        {
            presentation.ImageVariants = await assets.LoadImageVariantsCatalogAsync(cancellationToken);
            presentation._layers = options.CreateLayers(textures);
            presentation.Presenters = new AvaloniaGamePresenters(gameplay, page, presentation._layers);
            presentation._programs = new SkiaShaderEffectProgramResolver(
                new AssetManagerShaderEffectProgramSource(assets));
            var preload = await presentation._programs.PreloadAsync(effectPrograms, cancellationToken);
            GameLog.Logger.Information(
                "Preloaded {UsableEffectProgramCount}/{EffectProgramCount} effect programs in {ElapsedMilliseconds} ms",
                preload.UsableProgramCount, preload.RequestedProgramCount, preload.Cache.TotalLoadTime.TotalMilliseconds);
            presentation.Effects = new AvaloniaEffectRuntime(gameplay, presentation._programs, assets);
            presentation.Audio = options.CreateAudioHost(assets, files);
            presentation.AudioModule = new BuiltinAudioModule(presentation.Audio, configuration: audioConfiguration);
            presentation.Video = options.CreateVideoPresenter is { } createVideo
                ? createVideo(assets, gameplay, files) ?? throw new InvalidOperationException("The video presenter factory returned null.")
                : new AvaloniaVideoPresenter(assets, gameplay, files: files, sharedLibVlc: sharedLibVlc);
            using var registration = new GameRegistration();
            configure(registration, presentation);
            presentation.View = registration.BuildView();
            presentation.View.RequireInstructions(requiredInstructions);
            presentation.Restorer = new StateRestoreSession(presentation.View);
            return presentation;
        }
        catch (Exception error)
        {
            try { await presentation.DisposeAsync(); }
            catch (Exception cleanupError) { throw new AggregateException(error, cleanupError); }
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate) return new(_disposeTask ??= DisposeCoreAsync());
    }

    public async Task BeginCatchUpAsync(CancellationToken cancellationToken)
    {
        AudioModule.BeginCatchUp();
        await Presenters.BeginCatchUpAsync();
        await Restorer.SuspendAsync(cancellationToken);
    }

    public async Task EndCatchUpAsync(IGameContext runtime, CancellationToken cancellationToken)
    {
        await AvaloniaUiDispatcher.Instance.InvokeAsync(() =>
        {
            Effects.Clear();
            Presenters.ClearSceneObjects();
            _gameplay.ResetSceneVisuals();
            return Task.CompletedTask;
        });
        await Restorer.RestoreAsync(runtime, cancellationToken);
        await Presenters.EndCatchUpAsync(cancellationToken);
        AudioModule.EndCatchUp();
    }

    private async Task DisposeCoreAsync()
    {
        var cleanup = new GalNet.GameHost.ResourceCleanup();
        // Implementations borrow platform ports, so release the view first.
        await cleanup.RunAsync(() => Restorer?.DisposeAsync() ?? ValueTask.CompletedTask);
        await cleanup.RunAsync(() => View?.DisposeAsync() ?? ValueTask.CompletedTask);
        if (Video is IAsyncDisposable asyncVideo) await cleanup.RunAsync(asyncVideo.DisposeAsync);
        else cleanup.Run(() => (Video as IDisposable)?.Dispose());
        if (Audio is IAsyncDisposable asyncAudio) await cleanup.RunAsync(asyncAudio.DisposeAsync);
        else cleanup.Run(() => Audio?.Dispose());
        cleanup.Run(() =>
        {
            if (Effects is not null) Effects.Dispose(); // owns the program resolver
            else _programs?.Dispose();
        });
        cleanup.Run(() => Presenters?.Dispose());
        cleanup.Run(() => (_layers as IDisposable)?.Dispose());
        cleanup.ThrowIfFailed();
    }
}
