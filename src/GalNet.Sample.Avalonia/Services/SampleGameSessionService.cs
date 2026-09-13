using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Avalonia.GameView;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Core.Runtime;
using GalNet.Core.Settings;
using GalNet.Core.View;
using GalNet.Rendering.Scene;
using GalNet.Assets;
using GalNet.Assets.Provider;
using GalNet.Core.Assets;
using GalNet.Presentation.Abstractions.Runtime;
using GalNet.Runtime.Engine;
using GalNet.Runtime.Handlers;
using GalNet.Runtime.Logging;
using GalNet.Runtime.Runtime;
using GalNet.Sample.Avalonia.Presentation;
using GalNet.Storage.FileSystem;

namespace GalNet.Sample.Avalonia.Services;

/// <summary>Sample host implementation; all game/file-system work stays outside page VMs.</summary>
internal sealed partial class SampleGameSessionService : ObservableObject, IGameSessionService, IDisposable
{
    private readonly GamePageViewModel _gameplay;
    private readonly GamePage _page;
    private readonly ObservableCollection<GameSaveSlot> _saveSlots = [];
    private readonly ReadOnlyObservableCollection<GameSaveSlot> _readOnlySaveSlots;
    private DirectoryGameContentProvider? _contentProvider;
    private IAssetManager? _assets;
    private FileSaveService? _saves;
    private FileVariableService? _variables;
    private FileGameProgressService? _progress;
    private GameSettings? _settings;
    private GameEngine? _engine;
    private AvaloniaGamePageView? _pageView;
    private SampleLayerFactory? _layers;
    private SampleMediaViews? _media;
    private AvaloniaEffectRuntime? _effects;
    private string? _gameDirectory;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly GameRunCoordinator _run = new();
    private bool _disposed;

    public SampleGameSessionService(GamePageViewModel gameplay, GamePage page)
    {
        _gameplay = gameplay;
        _page = page;
        _readOnlySaveSlots = new ReadOnlyObservableCollection<GameSaveSlot>(_saveSlots);
        _gameplay.InteractionObserved += OnInteractionObserved;
    }

    public ReadOnlyObservableCollection<GameSaveSlot> SaveSlots => _readOnlySaveSlots;
    [ObservableProperty] private string _gameTitle = "GalNet Avalonia Sample";
    [ObservableProperty] private string _statusMessage = "Pass a published game directory when launching the sample.";
    [ObservableProperty] private bool _isReady;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private bool _canContinue;

    public async Task InitializeAsync(GameLaunchOptions options, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.GameDirectory))
        {
            StatusMessage = "Usage: GalNet.Sample.Avalonia <game-data-directory> [--profile <directory>]";
            GameLog.Logger.Warning("Session initialized without a game directory");
            return;
        }

        try
        {
            _gameDirectory = options.GameDirectory;
            var profileDirectory = options.ProfileDirectory ?? Path.Combine(_gameDirectory, ".galnet");
            _contentProvider = new DirectoryGameContentProvider(_gameDirectory);
            _assets = new AssetManager([new LocalFileProvider(_gameDirectory)]);
            _assets.RegisterDecoder(new SceneTextureAssetDecoder());
            var spriteFiles = await _assets.GetFilesAsync(ResourceType.Sprite, cancellationToken);
            var preloadResults = await Task.WhenAll(
                spriteFiles.Select(file => _assets.LoadAsync<SceneTexture>(file, cancellationToken)));
            var preloadFailures = preloadResults.Count(texture => texture is null);
            GameLog.Logger.Information("Preloaded {SpriteCount} sprite assets ({FailureCount} deferred to fallback)",
                spriteFiles.Count, preloadFailures);
            _saves = new FileSaveService(profileDirectory);
            _variables = await FileVariableService.CreateAsync(new FilePlayerVariableStore(profileDirectory), cancellationToken);
            _progress = new FileGameProgressService(profileDirectory);
            _settings = new GameSettings();
            await RefreshSlotsAsync(cancellationToken);
            IsReady = true;
            StatusMessage = $"Loaded game data: {_gameDirectory}";
            _gameplay.StatusMessage = "Ready";
            GameLog.Logger.Information("Session initialized. SaveSlots={SaveSlotCount}, CanContinue={CanContinue}",
                _saveSlots.Count, CanContinue);
        }
        catch (Exception exception)
        {
            StatusMessage = $"Unable to load game: {exception.Message}";
            GameLog.Logger.Error(exception, "Session initialization failed");
        }
    }

    public Task StartNewGameAsync(CancellationToken cancellationToken = default)
    {
        GameLog.Logger.Information("Starting a new game");
        return RestartAsync(null, cancellationToken);
    }

    public async Task ContinueAsync(CancellationToken cancellationToken = default)
    {
        var slot = _saveSlots
            .Where(candidate => !candidate.IsEmpty && !candidate.IsCorrupt)
            .OrderByDescending(candidate => candidate.Timestamp)
            .FirstOrDefault();
        if (slot is null)
        {
            StatusMessage = "There is no valid save to continue.";
            GameLog.Logger.Warning("Continue requested but no valid slot exists");
            return;
        }

        GameLog.Logger.Information("Continuing from slot {SlotIndex} at {Timestamp}", slot.SlotIndex, slot.Timestamp);
        await LoadAsync(slot.SlotIndex, cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return;
        GameLog.Logger.Information("Stopping active game flow");
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            if (!_disposed) await StopCurrentRunAsync();
        }
        finally { _lifecycle.Release(); }
    }

    public async Task SaveAsync(int slotIndex, CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            await EnsureEngineAsync(cancellationToken);
            await _saves!.SaveAsync(slotIndex, _engine!.CreateSaveData());
            _gameplay.StatusMessage = $"Saved to slot {slotIndex}.";
            await RefreshSlotsAsync(cancellationToken);
        }
        finally { _lifecycle.Release(); }
    }

    public async Task LoadAsync(int slotIndex, CancellationToken cancellationToken = default)
    {
        GameLog.Logger.Information("Loading slot {SlotIndex}", slotIndex);
        GameSnapshot? snapshot;
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            snapshot = await _saves!.LoadAsync(slotIndex);
            if (snapshot is null)
            {
                _gameplay.StatusMessage = $"Slot {slotIndex} is empty or invalid.";
                GameLog.Logger.Warning("Slot {SlotIndex} contained no valid snapshot", slotIndex);
                return;
            }

            await StopCurrentRunAsync();
            await DisposeEngineAsync();
            await EnsureEngineAsync(cancellationToken);
            _engine!.RestoreFrom(snapshot);
            await RestorePersistentEffectsAsync(cancellationToken);
            StartRun();
            _gameplay.StatusMessage = $"Loaded slot {slotIndex}.";
        }
        finally { _lifecycle.Release(); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        try { StopAsync().GetAwaiter().GetResult(); }
        finally
        {
            _disposed = true;
            _gameplay.InteractionObserved -= OnInteractionObserved;
            DisposeEngineAsync().GetAwaiter().GetResult();
            _assets?.Dispose();
            _assets = null;
        }
    }

    private async Task RestartAsync(GameSnapshot? snapshot, CancellationToken cancellationToken)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            await StopCurrentRunAsync();
            await DisposeEngineAsync();
            await EnsureEngineAsync(cancellationToken);
            if (snapshot is not null)
            {
                _engine!.RestoreFrom(snapshot);
                await RestorePersistentEffectsAsync(cancellationToken);
            }
            StartRun();
        }
        finally { _lifecycle.Release(); }
    }

    private void StartRun()
    {
        var engine = _engine ?? throw new InvalidOperationException("The game engine has not been initialized.");
        // Start synchronously until the first asynchronous engine wait. This lets the load
        // callback return only after the initial scene/dialogue has been submitted to the UI,
        // so the destination page does not begin its fade while its first frame is still being built.
        _run.Start(cancellationToken => RunEngineAsync(engine, cancellationToken));
        GameLog.Logger.Debug("Game engine run task created");
    }

    private async Task RunEngineAsync(GameEngine engine, CancellationToken cancellationToken)
    {
        try
        {
            await OnUiAsync(() =>
            {
                IsPlaying = true;
                _gameplay.StatusMessage = "Playing";
            });
            GameLog.Logger.Information("Game engine flow started");
            await engine.StepAsync(cancellationToken);
            await OnUiAsync(() => _gameplay.StatusMessage = "Game flow completed.");
            GameLog.Logger.Information("Game engine flow completed");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await OnUiAsync(() => _gameplay.StatusMessage = "Game flow was cancelled.");
            GameLog.Logger.Information("Game engine flow cancelled");
        }
        catch (Exception exception)
        {
            await OnUiAsync(() => _gameplay.StatusMessage = $"Game flow failed: {exception.Message}");
            GameLog.Logger.Error(exception, "Game engine flow failed");
        }
        finally
        {
            await OnUiAsync(() => IsPlaying = false);
            await RefreshSlotsAsync(CancellationToken.None);
        }
    }

    private async Task StopCurrentRunAsync()
    {
        GameLog.Logger.Debug("Cancellation requested for the active engine flow");
        await _run.StopAsync();
    }

    private async Task DisposeEngineAsync()
    {
        _pageView?.Dispose();
        _pageView = null;
        _layers?.Dispose();
        _layers = null;
        _media?.Dispose();
        _media = null;
        _effects?.Dispose();
        _effects = null;
        _engine = null;
        GameLog.Logger.Debug("Resetting scene presentation before creating the next game engine");
        await ResetScenePresentationAsync();
        GameLog.Logger.Debug("Scene presentation reset completed");
    }

    private Task ResetScenePresentationAsync()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            _gameplay.ResetScenePresentation();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                _gameplay.ResetScenePresentation();
                completion.TrySetResult();
            }
            catch (Exception exception) { completion.TrySetException(exception); }
        });
        return completion.Task;
    }

    private async Task EnsureEngineAsync(CancellationToken cancellationToken)
    {
        if (_engine is not null) return;
        if (!IsReady || _contentProvider is null || _assets is null || _variables is null || _progress is null || _settings is null || _gameDirectory is null)
            throw new InvalidOperationException("The game session is not initialized.");

        _layers = new SampleLayerFactory(_assets);
        _pageView = new AvaloniaGamePageView(_gameplay, _page, _layers);
        _media = new SampleMediaViews(_gameplay);
        _effects = new AvaloniaEffectRuntime(
            _gameplay,
            _layers,
            programs: new SkiaShaderEffectProgramResolver(new AssetManagerShaderEffectProgramSource(_assets)));
        var gameView = new CompositeGameView(_pageView, _pageView, _pageView, _media, _media, _effects, _pageView, _pageView, _pageView);
        var content = await _contentProvider.LoadAsync(cancellationToken);
        var settings = new SettingsContainer();
        settings.Set(_settings);
        var runtime = new GameRuntime(null, content.Graph.RootNodeId, settings, _variables);
        _engine = new GameEngine(content.Graph, runtime, gameView, EntryHandlerRegistry.CreateDefault(_progress), _progress);
    }

    private async Task RefreshSlotsAsync(CancellationToken cancellationToken)
    {
        if (_saves is null) return;
        var slots = await _saves.ListSlotsAsync(cancellationToken);
        await OnUiAsync(() =>
        {
            _saveSlots.Clear();
            foreach (var slot in slots.Take(12))
            {
                _saveSlots.Add(new GameSaveSlot(
                    slot.SlotIndex,
                    slot.Timestamp,
                    slot.IsCorrupt ? "Corrupt save" : slot.Timestamp == default ? "Empty" : "Saved game",
                    slot.Timestamp == default && !slot.IsCorrupt,
                    slot.IsCorrupt));
            }
            CanContinue = _saveSlots.Any(slot => !slot.IsEmpty && !slot.IsCorrupt);
        });
    }

    private async Task RestorePersistentEffectsAsync(CancellationToken cancellationToken)
    {
        if (_engine is null || _effects is null) return;
        foreach (var effect in _engine.Runtime.SceneState.ActiveEffects)
            await _effects.StartEffectAsync(new EffectRequest(effect.Id, effect.InstanceId, effect.TargetHandleId, effect.Order, effect.Parameters, effect.ProgramResource)
            {
                AnimationValues = effect.AnimationValues
            }, cancellationToken);
    }

    private static void OnInteractionObserved(string interaction) =>
        GameLog.Logger.Information("Player interaction: {Interaction}", interaction);

    private static Task OnUiAsync(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(() =>
        {
            try { action(); completion.SetResult(); }
            catch (Exception exception) { completion.SetException(exception); }
        });
        return completion.Task;
    }
}
