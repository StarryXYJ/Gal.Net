using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Core.Assets;
using GalNet.Core.Gallery;
using GalNet.Core.Runtime;
using GalNet.Primitives.Builtins;
using GalNet.Runtime.Logging;
using GalNet.Sample.Avalonia.Debug;

namespace GalNet.Sample.Avalonia.Services;

/// <summary>Sample host implementation; all game/file-system work stays outside page VMs.</summary>
internal sealed partial class SampleGameSessionService : ObservableObject, IGameSessionService, IPreparedGameSessionService, IGameGallerySession, IAsyncDisposable
{
    private readonly GamePageViewModel _gameplay;
    private readonly GamePage _page;
    private readonly SamplePlayerSettingsService _settings;
    private readonly SampleLocalizationService _localization;
    private readonly SampleStartupService _startup;
    private readonly SampleLibVlcRuntime _mediaRuntime;
    private readonly IResourceTypeCatalog _resourceTypes;
    private readonly IGalleryTypeCatalog _galleryTypes;
    private readonly ObservableCollection<GameSaveSlot> _saveSlots = [];
    private readonly ReadOnlyObservableCollection<GameSaveSlot> _readOnlySaveSlots;
    private SampleGameResourceScope? _resources;
    private SampleSaveSession? _saveSession;
    private SampleGameRunner? _runner;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private bool _disposed;

    public SampleGameSessionService(GamePageViewModel gameplay, GamePage page, SamplePlayerSettingsService settings,
        SampleLocalizationService localization, SampleStartupService startup, SampleLibVlcRuntime mediaRuntime)
    {
        _gameplay = gameplay;
        _page = page;
        _settings = settings;
        _localization = localization;
        _startup = startup;
        _mediaRuntime = mediaRuntime;
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

    public async Task<bool> InitializeAsync(GameLaunchOptions options,
        Func<string, CancellationToken, Task<string?>> selectLanguage, CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            return await InitializeGameResourcesAsync(options, selectLanguage, cancellationToken);
        }
        finally { _lifecycle.Release(); }
    }

    private async Task<bool> InitializeGameResourcesAsync(GameLaunchOptions options,
        Func<string, CancellationToken, Task<string?>> selectLanguage, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.GameDirectory))
        {
            StatusMessage = "Usage: GalNet.Sample.Avalonia <game-data-directory> [--profile <directory>]";
            GameLog.Logger.Warning("Session initialized without a game directory");
            return true;
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
                    cancellationToken, resources.MediaFiles);
            }
            catch
            {
                resources.Dispose();
                throw;
            }

            _resources = resources;
            _saveSession = saveSession;
            _localization.AttachGame(resources.Content);

            foreach (var diagnostic in resources.Content.Localization.Diagnostics)
                GameLog.Logger.Warning("Skipped localization source {Path}: {Reason}", diagnostic.RelativePath, diagnostic.Message);
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
            if (!await _startup.InitializeAsync(saveSession.Variables,
                    resources.Content.Settings.TargetLocale.Code, selectLanguage, cancellationToken))
                throw new OperationCanceledException("Language selection was cancelled.", cancellationToken);
            await _settings.AttachAsync(saveSession.Variables, saveSession.Settings, cancellationToken);
            await _settings.RestoreDisplayAsync(cancellationToken);

            IsReady = true;
            StatusMessage = $"Loaded game data: {resources.GameDirectory}";
            _gameplay.StatusMessage = "Ready";
            GameLog.Logger.Information("Session initialized. SaveSlots={SaveSlotCount}, CanContinue={CanContinue}",
                _saveSlots.Count, CanContinue);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            StatusMessage = $"Unable to load game: {exception.Message}";
            GameLog.Logger.Error(exception, "Session initialization failed");
            return false;
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
            var saveSession = _saveSession;
            await _settings.FlushAsync();
            if (saveSession is not null)
                await saveSession.ClearAsync(cancellationToken);
            await OnUiAsync(() =>
            {
                _gameplay.TextSpeed = saveSession?.Settings.TextSpeed ?? _gameplay.TextSpeed;
                _gameplay.IsNvlMode = false;
                _gameplay.IsUiHidden = false;
                _gameplay.StatusMessage = "Player state cleared. Restarting...";
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

    internal async Task<SampleDebugSaveResult> SaveDebugSnapshotAsync(
        SampleDebugSaveKind kind,
        bool overwriteWhenAllVisibleSlotsAreOccupied,
        CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            if (_runner is null || _saveSession is null)
                throw new InvalidOperationException("没有进行中的游戏可供存档。");

            var firstEmptySlot = _saveSlots
                .Where(slot => slot.IsEmpty)
                .OrderBy(slot => slot.SlotIndex)
                .FirstOrDefault();
            if (firstEmptySlot is null && !overwriteWhenAllVisibleSlotsAreOccupied)
                return SampleDebugSaveResult.RequiresOverwrite();

            var targetSlot = firstEmptySlot?.SlotIndex
                ?? _saveSlots.OrderBy(slot => slot.SlotIndex).FirstOrDefault()?.SlotIndex
                ?? throw new InvalidOperationException("没有可用的存档槽。");
            var snapshot = SampleDebugSnapshotFactory.Create(_runner.CreateSaveData(), kind);
            await _saveSession.SaveAsync(targetSlot, snapshot, cancellationToken);
            await RefreshSlotsAsync(cancellationToken);
            await OnUiAsync(() => _gameplay.StatusMessage = $"Debug saved to slot {targetSlot}.");
            GameLog.Logger.Information("Created {DebugSaveKind} debug save in slot {SlotIndex}", kind, targetSlot);
            return SampleDebugSaveResult.Saved(targetSlot);
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

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        try { await StopAsync(); }
        finally
        {
            _disposed = true;
            _gameplay.InteractionObserved -= OnInteractionObserved;
            await DisposeRunnerAsync();
            await _settings.FlushAsync();
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

            await _runner.StartPreparedAsync(cancellationToken);
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

    private Task ResetScenePresentationAsync() => OnUiAsync(_gameplay.ResetScenePresentation);

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
            _settings,
            _localization,
            _mediaRuntime.Instance,
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

    private static Task OnUiAsync(Action action) => AvaloniaUiDispatcher.Instance.InvokeAsync(() =>
    {
        action();
        return Task.CompletedTask;
    });
}
