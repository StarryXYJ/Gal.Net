using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Core.Runtime;
using GalNet.Core.Services;
using GalNet.Core.Settings;
using GalNet.Runtime.Engine;
using GalNet.Runtime.Logging;
using GalNet.Runtime.Runtime;
using LibVLCSharp.Shared;
using GalNet.Avalonia.GameHost;
using GalNet.GameHost;

namespace GalNet.Sample.Avalonia.Services;

/// <summary>Sample page binding and run ownership; the engine owns execution and terminal states.</summary>
internal sealed class SampleGameRunner : IGameRun
{
    private readonly GamePageViewModel _gameplay;
    private readonly GameRun _run;
    private GameEngine _engine => _run.Engine;
    private readonly AvaloniaGamePresentation _presentation;
    private readonly SamplePlayerSettingsService _playerSettings;
    private readonly Func<bool, string?, Task> _updateStateAsync;
    private readonly Func<Task> _runCompletedAsync;
    private readonly Func<Task> _resetPresentationAsync;
    private readonly Func<Action, Task> _onUiAsync;
    private Task _stateUpdate = Task.CompletedTask;
    private bool _disposed;

    private SampleGameRunner(
        GamePageViewModel gameplay, GameRun run, AvaloniaGamePresentation presentation, SamplePlayerSettingsService playerSettings,
        Func<bool, string?, Task> updateStateAsync, Func<Task> runCompletedAsync,
        Func<Task> resetPresentationAsync, Func<Action, Task> onUiAsync)
    {
        _gameplay = gameplay;
        _run = run;
        _presentation = presentation;
        _playerSettings = playerSettings;
        _playerSettings.SettingsChanged += OnPlayerSettingsChanged;
        OnPlayerSettingsChanged(playerSettings.RuntimeSettings);
        _updateStateAsync = updateStateAsync;
        _runCompletedAsync = runCompletedAsync;
        _resetPresentationAsync = resetPresentationAsync;
        _onUiAsync = onUiAsync;
        _gameplay.AdvanceRequested += OnAdvanceRequested;
        _gameplay.BindPlayback(_engine);
        _engine.StateChanged += OnEngineStateChanged;
    }

    public static async Task<SampleGameRunner> CreateAsync(
        GamePageViewModel gameplay, GamePage page, SampleGameResourceScope resources,
        SampleSaveSession saveSession, SamplePlayerSettingsService playerSettings, ITextResolver textResolver,
        LibVLC? sharedLibVlc, Func<bool, string?, Task> updateStateAsync,
        Func<Task> runCompletedAsync, Func<Task> resetPresentationAsync,
        Func<Action, Task> onUiAsync, CancellationToken cancellationToken)
    {
        var presentation = await AvaloniaGamePresentation.CreateAsync(gameplay, page, resources.Assets,
            resources.PreloadedTextures, resources.EffectPrograms, resources.MediaFiles,
            (registration, presentation) => SampleCustomization.ConfigureExecution(registration,
                presentation.Presenters, page, resources.Content, presentation.Effects,
                presentation.Audio, presentation.Video, presentation.AudioModule),
            resources.Content.Graph.Nodes.OfType<GalNet.Core.Graph.Group>()
                .SelectMany(group => group.Entries).Select(entry => entry.Type), sharedLibVlc, cancellationToken);
        GameRun? run = null;
        try
        {
            var settings = new SettingsContainer();
            settings.Set(resources.Content.Settings);
            settings.Set(saveSession.Settings);
            var runtime = new GameRuntime(textResolver, resources.Content.Graph.RootNodeId, settings, saveSession.Variables);
            run = new GameRun(resources.Content.Graph, runtime, presentation.View,
                saveSession.Progress, presentation.Presenters.ChoicePresenter, presentation, presentation.Restorer);
            return new SampleGameRunner(gameplay, run, presentation, playerSettings,
                updateStateAsync, runCompletedAsync, resetPresentationAsync, onUiAsync);
        }
        catch (Exception error)
        {
            List<Exception> errors = [error];
            try { if (run is not null) await run.DisposeAsync(); }
            catch (Exception cleanupError) { errors.Add(cleanupError); }
            try { await presentation.DisposeAsync(); }
            catch (Exception cleanupError) { errors.Add(cleanupError); }
            if (errors.Count > 1) throw new AggregateException(errors);
            throw;
        }
    }

    public GameSnapshot CreateSaveData() => _run.CreateSaveData();

    public Task PrepareAsync(GameSnapshot? snapshot, CancellationToken cancellationToken) =>
        _run.PrepareAsync(snapshot, cancellationToken);

    public async Task StartPreparedAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ = CompleteOpeningPresentationAsync();
        try { await _run.StartPreparedAsync(cancellationToken); }
        finally { _presentation.Presenters.CompleteInitialPresentation(); }
    }

    public async Task StopAsync()
    {
        await _run.StopAsync();
        _presentation.Presenters.CompleteInitialPresentation();
        await _stateUpdate;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _playerSettings.SettingsChanged -= OnPlayerSettingsChanged;
        var cleanup = new SampleCleanup();
        cleanup.Run(() => _gameplay.BindPlayback(null));
        _gameplay.AdvanceRequested -= OnAdvanceRequested;
        _engine.StateChanged -= OnEngineStateChanged;
        cleanup.Run(_presentation.Presenters.CompleteInitialPresentation);
        await cleanup.RunAsync(() => _run.DisposeAsync());
        await cleanup.RunAsync(() => new(_presentation.Restorer.SuspendAsync(CancellationToken.None)));
        await cleanup.RunAsync(() => new(_stateUpdate));
        await cleanup.RunAsync(() => new(_resetPresentationAsync()));
        await cleanup.RunAsync(() => _presentation.DisposeAsync());
        await cleanup.RunAsync(() => new(_updateStateAsync(false, null)));
        cleanup.ThrowIfFailed();
    }

    private async Task CompleteOpeningPresentationAsync()
    {
        try
        {
            await _presentation.Presenters.InitialPresentationReady;
            await _onUiAsync(() =>
            {
                if (!_disposed && _engine.State != GameEngineState.Stopped)
                    _gameplay.CompleteOpeningPresentation();
            });
        }
        catch (Exception error) { GameLog.Logger.Debug(error, "Opening presentation did not become interactive"); }
    }

    private void OnAdvanceRequested() => _ = AdvanceAsync();
    private void OnPlayerSettingsChanged(GameSettings settings) => _presentation.AudioModule.ApplyPlayerSettings(settings);

    private async Task AdvanceAsync()
    {
        try { await _engine.AdvanceAsync(); }
        catch (Exception error) { GameLog.Logger.Error(error, "Player advance failed"); }
    }

    private void OnEngineStateChanged(GameEngineStateChange change) =>
        _stateUpdate = ApplyStateAsync(_stateUpdate, change);

    private async Task ApplyStateAsync(Task previous, GameEngineStateChange change)
    {
        await previous;
        if (_disposed) return;
        try
        {
            var message = change.State switch
            {
                GameEngineState.Running => "Playing",
                GameEngineState.Completed => "Game flow completed.",
                GameEngineState.Faulted => $"Game flow failed: {change.Error?.Message}",
                GameEngineState.Stopped => "Game flow was cancelled.",
                _ => null
            };
            GameLog.Logger.Information("Game engine state changed to {State}", change.State);
            if (change.Error is { } error)
            {
                _presentation.Presenters.FailInitialPresentation(error);
                GameLog.Logger.Error(error, "Game engine flow failed");
            }
            await _updateStateAsync(change.State == GameEngineState.Running, message);
            if (change.State is GameEngineState.Completed or GameEngineState.Faulted)
            {
                _presentation.Presenters.CompleteInitialPresentation();
                await _runCompletedAsync();
            }
        }
        catch (Exception error) { GameLog.Logger.Error(error, "Game state UI update failed"); }
    }
}
