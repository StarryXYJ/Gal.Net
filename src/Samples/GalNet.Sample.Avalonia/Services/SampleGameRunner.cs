using GalNet.Assets;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Core.Runtime;
using GalNet.Core.Settings;
using GalNet.Presentation.Abstractions.Runtime;
using GalNet.Presentation.Abstractions.View;
using GalNet.Primitives.Builtins;
using GalNet.Rendering.Scene;
using GalNet.Runtime.Engine;
using GalNet.Runtime.Logging;
using GalNet.Runtime.Runtime;
using GalNet.Sample.Avalonia.Presentation;

namespace GalNet.Sample.Avalonia.Services;

internal sealed class SampleGameRunner : IAsyncDisposable
{
    private readonly GamePageViewModel _gameplay;
    private readonly GameEngine _engine;
    private readonly AvaloniaGamePageView _pageView;
    private readonly SampleLayerFactory _layers;
    private readonly SampleMediaViews _media;
    private readonly AvaloniaEffectRuntime _effects;
    private readonly PersistentSceneRestorer _restorer;
    private readonly Func<bool, string?, Task> _updateStateAsync;
    private readonly Func<Task> _runCompletedAsync;
    private readonly Func<Task> _resetPresentationAsync;
    private readonly Func<Action, Task> _onUiAsync;
    private readonly GameRunCoordinator _run = new();
    private bool _prepared;
    private bool _disposed;

    private SampleGameRunner(
        GamePageViewModel gameplay,
        GameEngine engine,
        AvaloniaGamePageView pageView,
        SampleLayerFactory layers,
        SampleMediaViews media,
        AvaloniaEffectRuntime effects,
        Func<bool, string?, Task> updateStateAsync,
        Func<Task> runCompletedAsync,
        Func<Task> resetPresentationAsync,
        Func<Action, Task> onUiAsync)
    {
        _gameplay = gameplay;
        _engine = engine;
        _pageView = pageView;
        _layers = layers;
        _media = media;
        _effects = effects;
        _restorer = new PersistentSceneRestorer(
            effects,
            pageView.ParticlePresenter,
            pageView.AnimationPresenter,
            pageView.LayerPresenter);
        _updateStateAsync = updateStateAsync;
        _runCompletedAsync = runCompletedAsync;
        _resetPresentationAsync = resetPresentationAsync;
        _onUiAsync = onUiAsync;
        _pageView.AdvanceRequested += OnAdvanceRequested;
    }

    public static async Task<SampleGameRunner> CreateAsync(
        GamePageViewModel gameplay,
        GamePage page,
        SampleGameResourceScope resources,
        SampleSaveSession saveSession,
        Func<bool, string?, Task> updateStateAsync,
        Func<Task> runCompletedAsync,
        Func<Task> resetPresentationAsync,
        Func<Action, Task> onUiAsync,
        CancellationToken cancellationToken)
    {
        SampleLayerFactory? layers = null;
        AvaloniaGamePageView? pageView = null;
        SampleMediaViews? media = null;
        SkiaShaderEffectProgramResolver? programs = null;
        AvaloniaEffectRuntime? effects = null;
        CompositeGameView? gameView = null;
        GameEngine? engine = null;
        try
        {
            layers = new SampleLayerFactory(resources.PreloadedTextures);
            pageView = new AvaloniaGamePageView(gameplay, page, layers);
            media = new SampleMediaViews(gameplay);
            programs = new SkiaShaderEffectProgramResolver(
                new AssetManagerShaderEffectProgramSource(resources.Assets));
            var preload = await programs.PreloadAsync(resources.EffectPrograms, cancellationToken);
            GameLog.Logger.Information(
                "Preloaded {UsableEffectProgramCount}/{EffectProgramCount} effect programs in {ElapsedMilliseconds} ms",
                preload.UsableProgramCount,
                preload.RequestedProgramCount,
                preload.Cache.TotalLoadTime.TotalMilliseconds);
            effects = new AvaloniaEffectRuntime(gameplay, programs: programs);
            gameView = new CompositeGameView(BuiltinEntryModules.CreateRecommended(
                pageView.DialoguePresenter,
                pageView.LayerPresenter,
                pageView.AnimationPresenter,
                effects,
                resources.Content.Gallery,
                pageView.ParticlePresenter));
            var settings = new SettingsContainer();
            settings.Set(saveSession.Settings);
            var runtime = new GameRuntime(
                null,
                resources.Content.Graph.RootNodeId,
                settings,
                saveSession.Variables);
            engine = new GameEngine(
                resources.Content.Graph,
                runtime,
                gameView,
                saveSession.Progress,
                pageView.ChoicePresenter);
            return new SampleGameRunner(
                gameplay,
                engine,
                pageView,
                layers,
                media,
                effects,
                updateStateAsync,
                runCompletedAsync,
                resetPresentationAsync,
                onUiAsync);
        }
        catch
        {
            engine?.Dispose();
            if (engine is null)
                gameView?.Dispose();
            pageView?.Dispose();
            layers?.Dispose();
            media?.Dispose();
            effects?.Dispose();
            if (effects is null)
                programs?.Dispose();
            throw;
        }
    }

    public GameSnapshot CreateSaveData() => _engine.CreateSaveData();

    public async Task PrepareAsync(GameSnapshot? snapshot, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (snapshot is not null)
        {
            _engine.RestoreFrom(snapshot);
            await _restorer.RestoreAsync(_engine.Runtime, cancellationToken);
        }

        _prepared = true;
        GameLog.Logger.Debug("Game runtime prepared; waiting for page transition before starting the engine flow");
    }

    public void StartPrepared()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_prepared)
            throw new InvalidOperationException("No prepared game is available to start.");

        _prepared = false;
        _run.Start(cancellationToken => RunEngineAsync(cancellationToken));
        _ = CompleteOpeningPresentationAsync();
        GameLog.Logger.Debug("Game engine run task created");
    }

    public async Task StopAsync()
    {
        GameLog.Logger.Debug("Cancellation requested for the active engine flow");
        await _run.StopAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await StopAsync();
        await _updateStateAsync(false, null);
        _pageView.AdvanceRequested -= OnAdvanceRequested;
        _engine.Dispose();
        _pageView.Dispose();
        _layers.Dispose();
        _media.Dispose();
        _effects.Dispose();
        _prepared = false;
        GameLog.Logger.Debug("Resetting scene presentation before creating the next game engine");
        await _resetPresentationAsync();
        GameLog.Logger.Debug("Scene presentation reset completed");
    }

    private async Task CompleteOpeningPresentationAsync()
    {
        try
        {
            await _pageView.InitialPresentationReady;
            if (!_disposed)
                await _onUiAsync(_gameplay.CompleteOpeningPresentation);
        }
        catch (Exception exception)
        {
            GameLog.Logger.Debug(exception, "Opening presentation did not reach an interactive boundary");
        }
    }

    private async Task RunEngineAsync(CancellationToken cancellationToken)
    {
        var finished = false;
        try
        {
            await _updateStateAsync(true, "Playing");
            GameLog.Logger.Information("Game engine flow started");
            finished = !await _engine.AdvanceAsync(cancellationToken);
            if (finished)
            {
                await _updateStateAsync(true, "Game flow completed.");
                GameLog.Logger.Information("Game engine flow completed");
            }
            else
            {
                GameLog.Logger.Debug("Game engine flow reached an interactive boundary");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            finished = true;
            _pageView.CompleteInitialPresentation();
            await _updateStateAsync(true, "Game flow was cancelled.");
            GameLog.Logger.Information("Game engine flow cancelled");
        }
        catch (Exception exception)
        {
            finished = true;
            _pageView.FailInitialPresentation(exception);
            await _updateStateAsync(true, $"Game flow failed: {exception.Message}");
            GameLog.Logger.Error(exception, "Game engine flow failed");
        }
        finally
        {
            _pageView.CompleteInitialPresentation();
            if (finished)
                await CompleteRunAsync();
        }
    }

    private void OnAdvanceRequested() => _ = AdvanceEngineAsync();

    private async Task AdvanceEngineAsync()
    {
        try
        {
            if (!await _engine.AdvanceAsync())
            {
                await _updateStateAsync(true, "Game flow completed.");
                GameLog.Logger.Information("Game engine flow completed");
                await CompleteRunAsync();
            }
        }
        catch (Exception exception)
        {
            _pageView.FailInitialPresentation(exception);
            await _updateStateAsync(true, $"Game flow failed: {exception.Message}");
            GameLog.Logger.Error(exception, "Player advance failed");
            await CompleteRunAsync();
        }
    }

    private async Task CompleteRunAsync()
    {
        if (_disposed) return;
        _pageView.CompleteInitialPresentation();
        await _updateStateAsync(false, null);
        await _runCompletedAsync();
    }
}
