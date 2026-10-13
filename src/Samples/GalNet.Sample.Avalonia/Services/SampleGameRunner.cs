using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Core.Runtime;
using GalNet.Core.Services;
using GalNet.Core.Settings;
using GalNet.Primitives.Builtins;
using GalNet.Runtime.Engine;
using GalNet.Runtime.Logging;
using GalNet.Runtime.Runtime;

namespace GalNet.Sample.Avalonia.Services;

/// <summary>Sample page binding and run ownership; the engine owns execution and terminal states.</summary>
internal sealed class SampleGameRunner : IAsyncDisposable
{
    private readonly GamePageViewModel _gameplay;
    private readonly GameEngine _engine;
    private readonly SampleGamePresentation _presentation;
    private readonly SamplePlayerSettingsService _playerSettings;
    private readonly PersistentSceneReplaySession _restorer;
    private readonly Func<bool, string?, Task> _updateStateAsync;
    private readonly Func<Task> _runCompletedAsync;
    private readonly Func<Task> _resetPresentationAsync;
    private readonly Func<Action, Task> _onUiAsync;
    private Task _stateUpdate = Task.CompletedTask;
    private bool _prepared;
    private bool _disposed;

    private SampleGameRunner(
        GamePageViewModel gameplay, GameEngine engine, SampleGamePresentation presentation, SamplePlayerSettingsService playerSettings,
        Func<bool, string?, Task> updateStateAsync, Func<Task> runCompletedAsync,
        Func<Task> resetPresentationAsync, Func<Action, Task> onUiAsync)
    {
        _gameplay = gameplay;
        _engine = engine;
        _presentation = presentation;
        _playerSettings = playerSettings;
        _playerSettings.SettingsChanged += OnPlayerSettingsChanged;
        OnPlayerSettingsChanged(playerSettings.RuntimeSettings);
        _restorer = presentation.Restorer;
        _updateStateAsync = updateStateAsync;
        _runCompletedAsync = runCompletedAsync;
        _resetPresentationAsync = resetPresentationAsync;
        _onUiAsync = onUiAsync;
        _gameplay.AdvanceRequested += OnAdvanceRequested;
        _gameplay.BindPlayback(engine);
        _engine.StateChanged += OnEngineStateChanged;
    }

    public static async Task<SampleGameRunner> CreateAsync(
        GamePageViewModel gameplay, GamePage page, SampleGameResourceScope resources,
        SampleSaveSession saveSession, SamplePlayerSettingsService playerSettings, ITextResolver textResolver, Func<bool, string?, Task> updateStateAsync,
        Func<Task> runCompletedAsync, Func<Task> resetPresentationAsync,
        Func<Action, Task> onUiAsync, CancellationToken cancellationToken)
    {
        var presentation = await SampleGamePresentation.CreateAsync(gameplay, page, resources, cancellationToken);
        GameEngine? engine = null;
        try
        {
            var settings = new SettingsContainer();
            settings.Set(resources.Content.Settings);
            settings.Set(saveSession.Settings);
            var runtime = new GameRuntime(textResolver, resources.Content.Graph.RootNodeId, settings, saveSession.Variables);
            engine = new GameEngine(resources.Content.Graph, runtime, presentation.View,
                saveSession.Progress, presentation.Presenters.ChoicePresenter, presentation);
            return new SampleGameRunner(gameplay, engine, presentation, playerSettings,
                updateStateAsync, runCompletedAsync, resetPresentationAsync, onUiAsync);
        }
        catch (Exception error)
        {
            List<Exception> errors = [error];
            try { if (engine is not null) await engine.DisposeAsync(); }
            catch (Exception cleanupError) { errors.Add(cleanupError); }
            try { await presentation.DisposeAsync(); }
            catch (Exception cleanupError) { errors.Add(cleanupError); }
            if (errors.Count > 1) throw new AggregateException(errors);
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
            await _engine.SynchronizePresentationAsync(_restorer.RestoreAsync, cancellationToken);
        }
        _prepared = true;
    }

    public async Task StartPreparedAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_prepared) throw new InvalidOperationException("No prepared game is available to start.");
        _prepared = false;
        _ = CompleteOpeningPresentationAsync();
        try { await _engine.AdvanceAsync(cancellationToken); }
        finally { _presentation.Presenters.CompleteInitialPresentation(); }
    }

    public async Task StopAsync()
    {
        await _engine.StopAsync();
        await _restorer.SuspendAsync(CancellationToken.None);
        _presentation.Presenters.CompleteInitialPresentation();
        await _stateUpdate;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _playerSettings.SettingsChanged -= OnPlayerSettingsChanged;
        _gameplay.BindPlayback(null);
        _gameplay.AdvanceRequested -= OnAdvanceRequested;
        _engine.StateChanged -= OnEngineStateChanged;
        _presentation.Presenters.CompleteInitialPresentation();
        List<Exception>? errors = null;
        await ReleaseAsync(() => _engine.DisposeAsync().AsTask());
        await ReleaseAsync(() => _restorer.SuspendAsync(CancellationToken.None));
        await ReleaseAsync(() => _stateUpdate);
        await ReleaseAsync(() => _presentation.DisposeAsync().AsTask());
        await ReleaseAsync(() => _updateStateAsync(false, null));
        await ReleaseAsync(_resetPresentationAsync);
        if (errors is not null) throw new AggregateException(errors);

        async Task ReleaseAsync(Func<Task> release)
        {
            try { await release(); }
            catch (Exception error) { (errors ??= []).Add(error); }
        }
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
