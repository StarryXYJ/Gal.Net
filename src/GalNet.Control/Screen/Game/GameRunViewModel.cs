using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GalNet.Control.View;
using GalNet.Core.Runtime;
using GalNet.Core.Services;
using GalNet.Core.Settings;
using GalNet.Runtime.Engine;
using GalNet.Runtime.Handlers;
using GalNet.Runtime.Loader;
using GalNet.Runtime.Runtime;
using Serilog;

namespace GalNet.Control.ViewModels;

public sealed class GameRunViewModel : IAsyncDisposable
{
    public DefaultGameView GameView { get; }
    public event Action<string>? CommandRequested;
    public GameSnapshot? CurrentSnapshot => _runtime?.CreateSnapshot();
    public async Task<bool> SaveCurrentAsync(int slot)
    {
        if (_saveService is null || CurrentSnapshot is not { } snapshot) return false;
        byte[]? preview = null;
        try { preview = await GameView.CapturePngAsync(includeUi: true); }
        catch (Exception ex) { Log.Warning(ex, "Could not capture save preview image"); }
        await _saveService.SaveAsync(slot, new SaveRequest { Snapshot = snapshot, PreviewImage = preview });
        return true;
    }

    private readonly ISettingsService _settingsService;
    private readonly IVariableService? _variableService;
    private readonly IGameContentProvider _contentProvider;
    private readonly ISaveService? _saveService;
    private readonly IGameProgressService? _progressService;
    private readonly Action? _onGameEnded;
    private readonly GameFlowOptions? _options;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private Task? _runTask;
    private int _stopped;
    private IGameRuntime? _runtime;

    public GameRunViewModel(
        DefaultGameView gameView,
        ISettingsService settingsService,
        IVariableService? variableService,
        IGameContentProvider contentProvider,
        ISaveService? saveService = null,
        IGameProgressService? progressService = null,
        GameFlowOptions? options = null,
        Action? onGameEnded = null)
    {
        GameView = gameView;
        _settingsService = settingsService;
        _variableService = variableService;
        _contentProvider = contentProvider;
        _saveService = saveService;
        _progressService = progressService;
        _options = options;
        _onGameEnded = onGameEnded;
        GameView.CommandRequested += command => CommandRequested?.Invoke(command);

    }

    /// <summary>Called by the host view after the game surface is attached to the visual tree.</summary>
    public void Start()
    {
        if (_runTask is null)
            _runTask = RunAsync(_lifetimeCts.Token);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 0)
            _lifetimeCts.Cancel();

        try { if (_runTask is not null) await _runTask; }
        catch (OperationCanceledException) { }
        _lifetimeCts.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        // Let the GameRunView attach DefaultGameView and complete its first layout before
        // presenters create dialogue controls. Without this, the first queued UI update can
        // be lost when a game starts immediately after navigation.
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
            () => { }, Avalonia.Threading.DispatcherPriority.Render, cancellationToken);
        var settings = new SettingsContainer();
        settings.Set(_settingsService.GetSnapshot());

        var completedNormally = false;
        try
        {
            var content = await _contentProvider.LoadAsync(cancellationToken);
            var graph = content.Graph;
            var runtime = new GameRuntime(
                GameView,
                null,
                _options?.StartNodeId ?? graph.RootNodeId,
                settings,
                _variableService);
            if (_options?.RestoreSnapshot is { } snapshot)
                runtime.RestoreFrom(snapshot);
            _runtime = runtime;
            var engine = new GameEngine(graph, runtime, registry: EntryHandlerRegistry.CreateDefault(_progressService), progress: _progressService);
            engine.CheckpointCreated += snapshot => _ = WriteQuickSaveAsync(snapshot);
            _options?.RuntimeCreated?.Invoke(runtime);
            _options?.GameStarted?.Invoke();
            await engine.StepAsync(cancellationToken);
            completedNormally = !cancellationToken.IsCancellationRequested;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to run game supplied by the content provider");
            _options?.GameFailed?.Invoke(ex);
        }
        finally
        {
            GameView.Cleanup();
            _runtime = null;
            if (completedNormally)
            {
                if (!_options!.IsGalleryPresentation && _saveService is not null)
                    await _saveService.DeleteQuickSaveAsync();
                _options?.GameEnded?.Invoke();
            }
            if (completedNormally && _onGameEnded is not null)
                Avalonia.Threading.Dispatcher.UIThread.Post(_onGameEnded);
        }
    }

    private async Task WriteQuickSaveAsync(GameSnapshot snapshot)
    {
        if (_options?.IsGalleryPresentation == true || _saveService is null) return;
        try { await _saveService.QuickSaveAsync(snapshot); }
        catch (Exception ex) { Log.Warning(ex, "Failed to write quick save"); }
    }

}
