using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Core.Assets;
using GalNet.Core.Gallery;
using GalNet.Core.Runtime;
using GalNet.Runtime.Logging;
using GalNet.Primitives.Builtins;

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
    private SampleGameResourceScope? _resources;
    private SampleSaveSession? _saveSession;
    private SampleGameRunner? _runner;
    private GameLaunchOptions? _launchOptions;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
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
        _launchOptions = options;
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            await InitializeGameResourcesAsync(options, cancellationToken);
        }
        finally { _lifecycle.Release(); }
    }

    internal async Task ReloadGameResourcesAsync(CancellationToken cancellationToken = default)
    {
        if (_launchOptions is null)
            throw new InvalidOperationException("The game session has not been initialized.");

        GameLog.Logger.Information("Reloading sample game resources");
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            await StopCurrentRunAsync();
            await DisposeRunnerAsync();
            await DisposeGameResourcesAsync();
            await InitializeGameResourcesAsync(_launchOptions, cancellationToken);
        }
        finally { _lifecycle.Release(); }
    }

    private async Task InitializeGameResourcesAsync(GameLaunchOptions options, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.GameDirectory))
        {
            StatusMessage = "Usage: GalNet.Sample.Avalonia <game-data-directory> [--profile <directory>]";
            GameLog.Logger.Warning("Session initialized without a game directory");
            return;
        }

        try
        {
            var resources = await SampleGameResourceScope.OpenAsync(
                options.GameDirectory,
                _resourceTypes,
                _galleryTypes,
                cancellationToken);
            var profileDirectory = options.ProfileDirectory ?? Path.Combine(resources.GameDirectory, ".galnet");
            SampleSaveSession? saveSession = null;
            try
            {
                saveSession = await SampleSaveSession.CreateAsync(
                    profileDirectory,
                    resources.Content,
                    resources.Assets,
                    cancellationToken);
            }
            catch
            {
                resources.Dispose();
                throw;
            }

            _resources = resources;
            _saveSession = saveSession;
            var preloadFailures = resources.SpriteFileCount - resources.PreloadedTextures.Count;
            GameLog.Logger.Information("Preloaded {SpriteCount} sprite assets ({FailureCount} deferred to fallback)",
                resources.SpriteFileCount, preloadFailures);
            GameLog.Logger.Information("Discovered {EffectProgramCount} effect program assets for session prewarming",
                resources.EffectPrograms.Count);
            GalleryDataSource = saveSession.GalleryDataSource;
            GalleryResources = saveSession.GalleryResources;
            OnPropertyChanged(nameof(GalleryDataSource));
            OnPropertyChanged(nameof(GalleryResources));
            await RefreshSlotsAsync(cancellationToken);
            IsReady = true;
            StatusMessage = $"Loaded game data: {resources.GameDirectory}";
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
            await DisposeRunnerAsync();
            var saveSession = _saveSession;
            if (saveSession is not null)
                await saveSession.ClearAsync(cancellationToken);
            await RefreshSlotsAsync(cancellationToken);
            await OnUiAsync(() =>
            {
                _gameplay.TextSpeed = saveSession?.Settings.TextSpeed ?? _gameplay.TextSpeed;
                _gameplay.IsNvlMode = false;
                _gameplay.IsUiHidden = false;
                _gameplay.StatusMessage = "Player state cleared.";
            });
        }
        finally { _lifecycle.Release(); }
    }

    public async Task SaveAsync(int slotIndex, CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            await EnsureRunnerAsync(cancellationToken);
            await _saveSession!.SaveAsync(slotIndex, _runner!.CreateSaveData(), cancellationToken);
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
        snapshot = await _saveSession!.LoadAsync(slotIndex, cancellationToken);
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
            DisposeRunnerAsync().GetAwaiter().GetResult();
            _saveSession?.Dispose();
            _saveSession = null;
            GalleryResources = null;
            GalleryDataSource = null;
            _resources?.Dispose();
            _resources = null;
        }
    }

    public async Task BeginPreparedGameAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            if (_runner is null)
                throw new InvalidOperationException("No prepared game is available to start.");

            _runner.StartPrepared();
        }
        finally { _lifecycle.Release(); }
    }

    private async Task PrepareGameAsync(GameSnapshot? snapshot, CancellationToken cancellationToken)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            await StopCurrentRunAsync();
            await DisposeRunnerAsync();
            await EnsureRunnerAsync(cancellationToken);
            await _runner!.PrepareAsync(snapshot, cancellationToken);
        }
        finally { _lifecycle.Release(); }
    }

    private async Task StopCurrentRunAsync()
    {
        if (_runner is not null)
            await _runner.StopAsync();
    }

    private async Task DisposeRunnerAsync()
    {
        var runner = _runner;
        _runner = null;
        if (runner is not null)
            await runner.DisposeAsync();
    }

    private async Task DisposeGameResourcesAsync()
    {
        _saveSession?.Dispose();
        _saveSession = null;
        GalleryResources = null;
        GalleryDataSource = null;
        OnPropertyChanged(nameof(GalleryResources));
        OnPropertyChanged(nameof(GalleryDataSource));

        _resources?.Dispose();
        _resources = null;

        await OnUiAsync(() =>
        {
            _saveSlots.Clear();
            IsReady = false;
            IsPlaying = false;
            CanContinue = false;
            StatusMessage = "Reloading game resources...";
            _gameplay.StatusMessage = "Reloading game resources...";
        });
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

    private async Task EnsureRunnerAsync(CancellationToken cancellationToken)
    {
        if (_runner is not null) return;
        var resources = _resources;
        var saveSession = _saveSession;
        if (!IsReady || resources is null || saveSession is null)
            throw new InvalidOperationException("The game session is not initialized.");

        _runner = await SampleGameRunner.CreateAsync(
            _gameplay,
            _page,
            resources,
            saveSession,
            UpdateRunnerStateAsync,
            () => RefreshSlotsAsync(CancellationToken.None),
            ResetScenePresentationAsync,
            OnUiAsync,
            cancellationToken);
    }

    private async Task RefreshSlotsAsync(CancellationToken cancellationToken)
    {
        if (_saveSession is null) return;
        var slots = await _saveSession.ListSlotsAsync(cancellationToken);
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

    private Task UpdateRunnerStateAsync(bool isPlaying, string? statusMessage)
    {
        return OnUiAsync(() =>
        {
            IsPlaying = isPlaying;
            if (statusMessage is not null)
                _gameplay.StatusMessage = statusMessage;
        });
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
