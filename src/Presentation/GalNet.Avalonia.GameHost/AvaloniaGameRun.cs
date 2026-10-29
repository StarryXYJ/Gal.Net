using GalNet.Avalonia.GameHost.Settings;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Core.Runtime;
using GalNet.Core.Services;
using GalNet.GameHost;
using GalNet.Runtime.Engine;
using GalNet.Runtime.Logging;
using GalNet.Runtime.Runtime;
using GalNet.Storage.FileSystem;
using LibVLCSharp.Shared;

namespace GalNet.Avalonia.GameHost;

/// <summary>Default page binding and run ownership; the engine owns execution and terminal states.</summary>
internal sealed class AvaloniaGameRun : IGameRun, IParameterizedGameRun
{
    private readonly GamePageViewModel _gameplay;
    private readonly GameRun _run;
    private GameEngine _engine => _run.Engine;
    private readonly AvaloniaGamePresentation _presentation;
    private readonly IAvaloniaPlayerSettings _playerSettings;
    private readonly Func<bool, string?, Task> _updateStateAsync;
    private readonly Func<Task> _runCompletedAsync;
    private readonly Func<Task> _resetPresentationAsync;
    private readonly Func<Action, Task> _onUiAsync;
    private readonly Func<Task> _flushPlayerVariables;
    private Task _stateUpdate = Task.CompletedTask;
    private Task _settingsUpdate = Task.CompletedTask;
    private bool _disposed;
    private IDisposable? _runBinding;
    private readonly Func<GameEngine, IDisposable>? _attachRun;
    private DialogueHistoryDispatcher _history = null!;

    private AvaloniaGameRun(
        GamePageViewModel gameplay, GameRun run, AvaloniaGamePresentation presentation, IAvaloniaPlayerSettings playerSettings,
        Func<bool, string?, Task> updateStateAsync, Func<Task> runCompletedAsync,
        Func<Task> resetPresentationAsync, Func<Action, Task> onUiAsync, Func<Task> flushPlayerVariables, Func<GameEngine, IDisposable>? attachRun)
    {
        _attachRun = attachRun;
        _gameplay = gameplay;
        _run = run;
        _presentation = presentation;
        _playerSettings = playerSettings;
        _updateStateAsync = updateStateAsync;
        _runCompletedAsync = runCompletedAsync;
        _resetPresentationAsync = resetPresentationAsync;
        _onUiAsync = onUiAsync;
        _flushPlayerVariables = flushPlayerVariables;
        OnPlayerSettingsChanged();
        _playerSettings.SettingsChanged += OnPlayerSettingsChanged;
        _gameplay.AdvanceRequested += OnAdvanceRequested;
        _gameplay.BindPlayback(_engine);
        _engine.StateChanged += OnEngineStateChanged;
        _gameplay.GetHistoryLines = () => { _history.FlushCompleted(); return _presentation.History.Lines; };
        _gameplay.PlayVoiceAsync = PlayVoiceAsync;
        _gameplay.StopVoiceReplayRequested += OnStopVoiceReplay;
        _presentation.VoiceReplay.Changed += OnVoiceReplayChanged;
        _presentation.VoiceReplay.Failed += OnVoiceReplayFailed;
        _presentation.History.Changed += OnHistoryChanged;
    }

    public static async Task<AvaloniaGameRun> CreateAsync(
        GamePageViewModel gameplay, GamePage page, GameResourceScope resources, AvaloniaGameResources visuals,
        PlayerSession saveSession, IAvaloniaPlayerSettings playerSettings, ITextResolver textResolver, AvaloniaGameHostOptions options,
        GalNet.Contracts.Builtins.Audio.AudioConfiguration audioConfiguration, LibVLC? sharedLibVlc, Func<bool, string?, Task> updateStateAsync,
        Func<Task> runCompletedAsync, Func<Task> resetPresentationAsync,
        Func<Action, Task> onUiAsync, CancellationToken cancellationToken)
    {
        var presentation = await AvaloniaGamePresentation.CreateAsync(gameplay, page, resources.Assets,
            visuals.PreloadedTextures, visuals.EffectPrograms, resources.MediaFiles,
            (registration, presentation) => options.ConfigureExecution(registration, presentation, page, resources.Content),
            resources.Content.Graph.Nodes.OfType<GalNet.Core.Graph.Group>()
                .SelectMany(group => group.Entries).Select(entry => entry.Type), options.Presentation, sharedLibVlc,
            audioConfiguration: audioConfiguration, cancellationToken: cancellationToken);
        GameRun? run = null;
        AvaloniaGameRun? adapter = null;
        try
        {
            var runtime = new GameRuntime(textResolver, resources.Content.Graph.RootNodeId, saveSession.Variables,
                saveDefaults: resources.Content.Graph.SaveVariables, playerDefaults: resources.Content.Graph.PlayerVariables);
            var history = new DialogueHistoryDispatcher(presentation.View, presentation.History, saveSession.Progress, resources.Content.Localization.Characters);
            run = new GameRun(resources.Content.Graph, runtime, history,
                saveSession.Progress, presentation.Presenters.ChoicePresenter, presentation, presentation.Restorer,
                playbackOptions: playerSettings.PlaybackOptions);
            adapter = new AvaloniaGameRun(gameplay, run, presentation, playerSettings,
                updateStateAsync, runCompletedAsync, resetPresentationAsync, onUiAsync, () => saveSession.Variables.FlushPlayerVariablesAsync(), options.AttachRun);
            adapter._history = history;
            history.SuppressRecording = () => run.Engine.IsDebugReplaying;
            history.TextStarting += adapter.OnTextStarting;
            history.StoryContinuing += adapter.OnStoryContinuing;
            return adapter;
        }
        catch (Exception error)
        {
            List<Exception> errors = [error];
            if (adapter is not null)
            {
                try { await adapter.DisposeAsync(); }
                catch (Exception cleanupError) { errors.Add(cleanupError); }
                if (errors.Count > 1) throw new AggregateException(errors);
                throw;
            }
            try { if (run is not null) await run.DisposeAsync(); }
            catch (Exception cleanupError) { errors.Add(cleanupError); }
            try { await presentation.DisposeAsync(); }
            catch (Exception cleanupError) { errors.Add(cleanupError); }
            if (errors.Count > 1) throw new AggregateException(errors);
            throw;
        }
    }

    public GameSnapshot CreateSaveData()
    {
        _history.FlushCompleted();
        return _presentation.History.Attach(_run.CreateSaveData());
    }
    public Task InitializeSaveVariablesAsync(IReadOnlyDictionary<string, object> variables, CancellationToken cancellationToken) =>
        _run.InitializeSaveVariablesAsync(variables, cancellationToken);
    public Task ValidateSnapshotAsync(GameSnapshot snapshot, CancellationToken cancellationToken)
    {
        snapshot = DialogueHistoryJournal.PrepareSnapshot(snapshot);
        DialogueHistoryJournal.Validate(snapshot);
        return _run.ValidateSnapshotAsync(snapshot, cancellationToken);
    }

    public async Task PrepareAsync(GameSnapshot? snapshot, CancellationToken cancellationToken)
    {
        if (snapshot is not null) snapshot = DialogueHistoryJournal.PrepareSnapshot(snapshot);
        _presentation.History.Restore(snapshot);
        await _run.PrepareAsync(snapshot, cancellationToken);
        _runBinding = _attachRun?.Invoke(_engine);
    }

    public async Task StartPreparedAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ = CompleteOpeningPresentationAsync();
        try { await _run.StartPreparedAsync(cancellationToken); }
        finally { _presentation.Presenters.CompleteInitialPresentation(); }
    }

    public async Task StopAsync()
    {
        OnStopVoiceReplay();
        var cleanup = new ResourceCleanup();
        await cleanup.RunAsync(() => new(_settingsUpdate));
        await cleanup.RunAsync(() => new(_run.StopAsync()));
        var binding = _runBinding;
        _runBinding = null;
        await cleanup.RunAsync(() => ResourceCleanup.DisposeAsync(binding));
        cleanup.Run(_presentation.Presenters.CompleteInitialPresentation);
        await cleanup.RunAsync(() => new(_stateUpdate));
        await cleanup.RunAsync(() => new(_flushPlayerVariables()));
        cleanup.ThrowIfFailed();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        OnStopVoiceReplay();
        _history.TextStarting -= OnTextStarting;
        _history.StoryContinuing -= OnStoryContinuing;
        _gameplay.StopVoiceReplayRequested -= OnStopVoiceReplay;
        _presentation.VoiceReplay.Changed -= OnVoiceReplayChanged;
        _presentation.VoiceReplay.Failed -= OnVoiceReplayFailed;
        _presentation.History.Changed -= OnHistoryChanged;
        _gameplay.GetHistoryLines = null;
        _gameplay.PlayVoiceAsync = null;
        await _onUiAsync(_gameplay.ResetDialogueHistory);
        _playerSettings.SettingsChanged -= OnPlayerSettingsChanged;
        var cleanup = new ResourceCleanup();
        cleanup.Run(() => _gameplay.BindPlayback(null));
        _gameplay.AdvanceRequested -= OnAdvanceRequested;
        _engine.StateChanged -= OnEngineStateChanged;
        cleanup.Run(_presentation.Presenters.CompleteInitialPresentation);
        await cleanup.RunAsync(() => new(_settingsUpdate));
        await cleanup.RunAsync(() => _run.DisposeAsync());
        await cleanup.RunAsync(() => new(_flushPlayerVariables()));
        await cleanup.RunAsync(() => ResourceCleanup.DisposeAsync(_runBinding));
        _runBinding = null;
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

    private void OnAdvanceRequested() { OnStopVoiceReplay(); _ = AdvanceAsync(); }

    private GalNet.Primitives.Builtins.Audio.AudioPlaybackTicket? _originalVoice;
    private void OnStopVoiceReplay() => _presentation.VoiceReplay.Stop();
    private void OnStoryContinuing()
    {
        if (_disposed) return;
        OnStopVoiceReplay();
        _ = _onUiAsync(() => { if (!_disposed) _gameplay.CloseHistoryCommand.Execute(null); });
    }
    private void OnVoiceReplayChanged() => _ = _onUiAsync(() => { if (!_disposed) _gameplay.IsVoiceReplaying = _presentation.VoiceReplay.IsPlaying; });
    private void OnVoiceReplayFailed(Exception error) => _ = _onUiAsync(() => { if (!_disposed) _gameplay.StatusMessage = error.Message; });
    private void OnHistoryChanged() => _ = _onUiAsync(() =>
    {
        if (!_disposed && _gameplay.IsHistoryOpen) _gameplay.HistoryLines = _presentation.History.Lines;
    });
    private Task PlayVoiceAsync(GalNet.Presentation.Abstractions.Application.DialogueVoice voice)
    {
        _originalVoice?.Stop();
        return _presentation.VoiceReplay.PlayAsync(voice);
    }
    private void OnTextStarting(GalNet.Presentation.Abstractions.Application.DialogueHistoryLine line, GalNet.Primitives.Builtins.Audio.AudioPlaybackTicket? ticket)
    {
        if (_disposed) return;
        _originalVoice = ticket;
        _presentation.Presenters.SetReadingContext(line.WasRead);
        _ = _onUiAsync(() => { if (!_disposed) _gameplay.SetCurrentDialogue(line, ticket?.IsCompleted != false); });
        if (ticket is not null) _ = CompleteVoiceAsync(line, ticket.Completion);
    }
    private async Task CompleteVoiceAsync(GalNet.Presentation.Abstractions.Application.DialogueHistoryLine line, Task completion)
    {
        await completion;
        await _onUiAsync(() => { if (!_disposed) _gameplay.CompleteDialogueVoice(line); });
    }
    private void OnPlayerSettingsChanged()
    {
        _presentation.AudioModule.ApplyMix(_playerSettings.AudioMix);
        _gameplay.TextSpeed = _playerSettings.TextSpeed;
        _settingsUpdate = ApplyPlaybackOptionsAsync(_settingsUpdate);
    }

    private async Task ApplyPlaybackOptionsAsync(Task previous)
    {
        await previous;
        if (_disposed) return;
        try { await _engine.UpdatePlaybackOptionsAsync(_playerSettings.PlaybackOptions); }
        catch (Exception error) { GameLog.Logger.Error(error, "Player playback settings failed"); }
    }

    private async Task AdvanceAsync()
    {
        try { await _engine.AdvanceAsync(); }
        catch (Exception error) { GameLog.Logger.Error(error, "Player advance failed"); }
    }

    private void OnEngineStateChanged(GameEngineStateChange change)
    {
        if (change.State != GameEngineState.Running) OnStopVoiceReplay();
        _stateUpdate = ApplyStateAsync(_stateUpdate, change);
    }

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
            // Restore/Seek temporarily prepares the engine without ending the active host run.
            if (change.State == GameEngineState.Ready) return;
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
