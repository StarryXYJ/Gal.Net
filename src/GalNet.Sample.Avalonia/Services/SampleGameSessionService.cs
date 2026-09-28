using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Avalonia.GameView;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Core.Runtime;
using GalNet.Core.Gallery;
using GalNet.Core.Scene;
using GalNet.Core.Services;
using GalNet.Core.Settings;
using GalNet.Core.View;
using GalNet.Rendering.Scene;
using GalNet.Assets;
using GalNet.Assets.Provider;
using GalNet.Core.Assets;
using GalNet.Presentation.Abstractions.Runtime;
using GalNet.Runtime.Engine;
using GalNet.Runtime.Logging;
using GalNet.Runtime.Runtime;
using GalNet.Primitives.Builtins;
using GalNet.Sample.Avalonia.Presentation;
using GalNet.Storage.FileSystem;

namespace GalNet.Sample.Avalonia.Services;

/// <summary>Sample host implementation; all game/file-system work stays outside page VMs.</summary>
internal sealed partial class SampleGameSessionService : ObservableObject, IGameSessionService, IPreparedGameSessionService, IGameGallerySession, IDisposable
{
    private readonly GamePageViewModel _gameplay;
    private readonly GamePage _page;
    private readonly IResourceTypeCatalog _resourceTypes;
    private readonly IGalleryTypeCatalog _galleryTypes;
    private readonly ObservableCollection<GameSaveSlot> _saveSlots = [];
    private readonly ReadOnlyObservableCollection<GameSaveSlot> _readOnlySaveSlots;
    private IGameContentProvider? _contentProvider;
    private GameContent? _content;
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
    private EffectProgramResource[] _effectPrograms = [];
    private readonly Dictionary<string, AssetHandle<SceneTexture>> _preloadedTextures = new(StringComparer.OrdinalIgnoreCase);
    private string? _gameDirectory;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly GameRunCoordinator _run = new();
    private bool _hasPreparedGame;
    private bool _disposed;

    public SampleGameSessionService(GamePageViewModel gameplay, GamePage page)
    {
        _gameplay = gameplay;
        _page = page;
        _resourceTypes = BuiltinResourceTypes.CreateCatalog();
        _galleryTypes = BuiltinGalleryTypes.CreateCatalog(_resourceTypes);
        _readOnlySaveSlots = new ReadOnlyObservableCollection<GameSaveSlot>(_saveSlots);
        _gameplay.InteractionObserved += OnInteractionObserved;
    }

    public ReadOnlyObservableCollection<GameSaveSlot> SaveSlots => _readOnlySaveSlots;
    public IGalleryDataSource? GalleryDataSource { get; private set; }
    public IGalleryResourceResolver? GalleryResources { get; private set; }
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
            var installation = await GameInstallation.OpenAsync(options.GameDirectory, cancellationToken);
            _gameDirectory = installation.RootDirectory;
            var profileDirectory = options.ProfileDirectory ?? Path.Combine(_gameDirectory, ".galnet");
            _contentProvider = installation.CreateContentProvider(_resourceTypes, _galleryTypes);
            _content = await _contentProvider.LoadAsync(cancellationToken);
            _assets = new AssetManager(installation.CreateAssetProviders(_resourceTypes));
            _assets.RegisterDecoder<SceneTexture>("sprite", new SceneTextureAssetDecoder());
            var spriteFiles = await _assets.GetFilesAsync("sprite", cancellationToken);
            var preloadResults = await Task.WhenAll(
                spriteFiles.Select(file => _assets.AcquireAsync<SceneTexture>(file.Id, cancellationToken)));
            foreach (var handle in preloadResults.OfType<AssetHandle<SceneTexture>>())
                _preloadedTextures.Add(handle.AssetId, handle);
            var preloadFailures = preloadResults.Count(handle => handle is null);
            GameLog.Logger.Information("Preloaded {SpriteCount} sprite assets ({FailureCount} deferred to fallback)",
                spriteFiles.Count, preloadFailures);
            var effectProgramFiles = await _assets.GetFilesAsync("effect-program", cancellationToken);
            _effectPrograms = effectProgramFiles.Select(file => new EffectProgramResource(file.Id)).ToArray();
            GameLog.Logger.Information("Discovered {EffectProgramCount} effect program assets for session prewarming",
                _effectPrograms.Length);
            _saves = new FileSaveService(profileDirectory);
            _variables = await FileVariableService.CreateAsync(new FilePlayerVariableStore(profileDirectory), cancellationToken);
            _variables.ConfigureSystemVariables(GalleryUnlockVariable.CreateDefinitions(_content.Gallery));
            GalleryDataSource = new GalleryDataSource(_content.Gallery, _variables);
            GalleryResources = await AssetGalleryResourceResolver.CreateAsync(_assets, _content.Gallery, cancellationToken);
            OnPropertyChanged(nameof(GalleryDataSource));
            OnPropertyChanged(nameof(GalleryResources));
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

    public async Task StartNewGameAsync(CancellationToken cancellationToken = default)
    {
        await PrepareNewGameAsync(cancellationToken);
        await BeginPreparedGameAsync(cancellationToken);
    }

    public async Task ContinueAsync(CancellationToken cancellationToken = default)
    {
        await PrepareContinueAsync(cancellationToken);
        await BeginPreparedGameAsync(cancellationToken);
    }

    public Task PrepareNewGameAsync(CancellationToken cancellationToken = default)
    {
        GameLog.Logger.Information("Preparing a new game");
        return PrepareGameAsync(null, cancellationToken);
    }

    public async Task PrepareContinueAsync(CancellationToken cancellationToken = default)
    {
        var slot = _saveSlots
            .Where(candidate => !candidate.IsEmpty && !candidate.IsCorrupt)
            .OrderByDescending(candidate => candidate.Timestamp)
            .FirstOrDefault();
        if (slot is null)
        {
            StatusMessage = "There is no valid save to continue.";
            GameLog.Logger.Warning("Continue requested but no valid slot exists");
            throw new InvalidOperationException("There is no valid save to continue.");
        }

        GameLog.Logger.Information("Preparing continuation from slot {SlotIndex} at {Timestamp}", slot.SlotIndex, slot.Timestamp);
        await PrepareLoadAsync(slot.SlotIndex, cancellationToken);
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

    internal async Task ClearPlayerStateAsync(CancellationToken cancellationToken = default)
    {
        GameLog.Logger.Information("Clearing all sample player state");
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            await StopCurrentRunAsync();
            await DisposeEngineAsync();
            if (_saves is not null)
                await _saves.ClearAsync(cancellationToken);
            if (_variables is not null)
                await _variables.ResetPlayerVariablesAsync(cancellationToken);
            _progress?.Clear();
            await RefreshSlotsAsync(cancellationToken);
            await OnUiAsync(() => _gameplay.StatusMessage = "Player state cleared.");
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
        await PrepareLoadAsync(slotIndex, cancellationToken);
        await BeginPreparedGameAsync(cancellationToken);
        _gameplay.StatusMessage = $"Loaded slot {slotIndex}.";
    }

    public async Task PrepareLoadAsync(int slotIndex, CancellationToken cancellationToken = default)
    {
        GameLog.Logger.Information("Loading slot {SlotIndex}", slotIndex);
        GameSnapshot? snapshot;
        snapshot = await _saves!.LoadAsync(slotIndex);
        if (snapshot is null)
        {
            _gameplay.StatusMessage = $"Slot {slotIndex} is empty or invalid.";
            GameLog.Logger.Warning("Slot {SlotIndex} contained no valid snapshot", slotIndex);
            throw new InvalidOperationException($"Slot {slotIndex} is empty or invalid.");
        }

        await PrepareGameAsync(snapshot, cancellationToken);
        _gameplay.StatusMessage = $"Loaded slot {slotIndex}.";
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
            (GalleryResources as IDisposable)?.Dispose();
            GalleryResources = null;
            foreach (var handle in _preloadedTextures.Values) handle.Dispose();
            _preloadedTextures.Clear();
            _assets?.Dispose();
            _assets = null;
        }
    }

    public async Task BeginPreparedGameAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            if (!_hasPreparedGame || _engine is null)
                throw new InvalidOperationException("No prepared game is available to start.");

            _hasPreparedGame = false;
            StartPreparedRun();
        }
        finally { _lifecycle.Release(); }
    }

    private async Task PrepareGameAsync(GameSnapshot? snapshot, CancellationToken cancellationToken)
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
            _hasPreparedGame = true;
            GameLog.Logger.Debug("Game runtime prepared; waiting for page transition before starting the engine flow");
        }
        finally { _lifecycle.Release(); }
    }

    private void StartPreparedRun()
    {
        var engine = _engine ?? throw new InvalidOperationException("The game engine has not been initialized.");
        var pageView = _pageView ?? throw new InvalidOperationException("The game page has not been initialized.");
        _run.Start(cancellationToken => RunEngineAsync(engine, cancellationToken));
        _ = CompleteOpeningPresentationAsync(pageView);
        GameLog.Logger.Debug("Game engine run task created");
    }

    private async Task CompleteOpeningPresentationAsync(AvaloniaGamePageView pageView)
    {
        try
        {
            await pageView.InitialPresentationReady;
            await OnUiAsync(() =>
            {
                if (ReferenceEquals(_pageView, pageView))
                    _gameplay.CompleteOpeningPresentation();
            });
        }
        catch (Exception exception)
        {
            GameLog.Logger.Debug(exception, "Opening presentation did not reach an interactive boundary");
        }
    }

    private async Task RunEngineAsync(GameEngine engine, CancellationToken cancellationToken)
    {
        var finished = false;
        try
        {
            await OnUiAsync(() =>
            {
                IsPlaying = true;
                _gameplay.StatusMessage = "Playing";
            });
            GameLog.Logger.Information("Game engine flow started");
            finished = !await engine.AdvanceAsync(cancellationToken);
            if (finished)
            {
                await OnUiAsync(() => _gameplay.StatusMessage = "Game flow completed.");
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
            _pageView?.CompleteInitialPresentation();
            await OnUiAsync(() => _gameplay.StatusMessage = "Game flow was cancelled.");
            GameLog.Logger.Information("Game engine flow cancelled");
        }
        catch (Exception exception)
        {
            finished = true;
            _pageView?.FailInitialPresentation(exception);
            await OnUiAsync(() => _gameplay.StatusMessage = $"Game flow failed: {exception.Message}");
            GameLog.Logger.Error(exception, "Game engine flow failed");
        }
        finally
        {
            _pageView?.CompleteInitialPresentation();
            if (finished)
                await CompleteRunAsync(engine);
        }
    }

    private async Task StopCurrentRunAsync()
    {
        GameLog.Logger.Debug("Cancellation requested for the active engine flow");
        await _run.StopAsync();
    }

    private async Task DisposeEngineAsync()
    {
        if (_pageView is not null) _pageView.AdvanceRequested -= OnAdvanceRequested;
        _engine?.Dispose();
        _pageView?.Dispose();
        _pageView = null;
        _layers?.Dispose();
        _layers = null;
        _media?.Dispose();
        _media = null;
        _effects?.Dispose();
        _effects = null;
        _engine = null;
        _hasPreparedGame = false;
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
        if (!IsReady || _content is null || _assets is null || _variables is null || _progress is null || _settings is null || _gameDirectory is null)
            throw new InvalidOperationException("The game session is not initialized.");

        _layers = new SampleLayerFactory(_preloadedTextures);
        _pageView = new AvaloniaGamePageView(_gameplay, _page, _layers);
        _media = new SampleMediaViews(_gameplay);
        var programs = new SkiaShaderEffectProgramResolver(new AssetManagerShaderEffectProgramSource(_assets));
        var preload = await programs.PreloadAsync(_effectPrograms, cancellationToken);
        GameLog.Logger.Information(
            "Preloaded {UsableEffectProgramCount}/{EffectProgramCount} effect programs in {ElapsedMilliseconds} ms",
            preload.UsableProgramCount,
            preload.RequestedProgramCount,
            preload.Cache.TotalLoadTime.TotalMilliseconds);
        _effects = new AvaloniaEffectRuntime(
            _gameplay,
            programs: programs);
        var gameView = new CompositeGameView(BuiltinEntryModules.CreateRecommended(
            _pageView, _pageView, _pageView, _effects, _content.Gallery, _pageView));
        var settings = new SettingsContainer();
        settings.Set(_settings);
        var runtime = new GameRuntime(null, _content.Graph.RootNodeId, settings, _variables);
        _engine = new GameEngine(_content.Graph, runtime, gameView, _progress, _pageView);
        _pageView.AdvanceRequested += OnAdvanceRequested;
    }

    private void OnAdvanceRequested()
    {
        if (_engine is { } engine)
            _ = AdvanceEngineAsync(engine);
    }

    private async Task AdvanceEngineAsync(GameEngine engine)
    {
        try
        {
            if (!await engine.AdvanceAsync())
            {
                await OnUiAsync(() => _gameplay.StatusMessage = "Game flow completed.");
                GameLog.Logger.Information("Game engine flow completed");
                await CompleteRunAsync(engine);
            }
        }
        catch (Exception exception)
        {
            _pageView?.FailInitialPresentation(exception);
            await OnUiAsync(() => _gameplay.StatusMessage = $"Game flow failed: {exception.Message}");
            GameLog.Logger.Error(exception, "Player advance failed");
            await CompleteRunAsync(engine);
        }
    }

    private async Task CompleteRunAsync(GameEngine engine)
    {
        if (!ReferenceEquals(_engine, engine)) return;
        _pageView?.CompleteInitialPresentation();
        await OnUiAsync(() => IsPlaying = false);
        await RefreshSlotsAsync(CancellationToken.None);
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
