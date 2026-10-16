using GalNet.Media.LibVlc;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.GameHost;
using GalNet.Storage.FileSystem;
using GalNet.Avalonia.GameHost;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Core.Gallery;
using GalNet.Core.Runtime;
using GalNet.Runtime.Logging;
using GalNet.Sample.Avalonia.Debug;

namespace GalNet.Sample.Avalonia.Services;

/// <summary>Sample host implementation; all game/file-system work stays outside page VMs.</summary>
internal sealed partial class SampleGameSessionService : ObservableObject, IGameSessionService, IPreparedGameSessionService, IFirstEmptySlotSession, IGameGallerySession, IAsyncDisposable
{
    private readonly GamePageViewModel _gameplay;
    private readonly GamePage _page;
    private readonly AvaloniaPlayerSettingsService _settings;
    private readonly SampleLocalizationService _localization;
    private readonly AvaloniaPlayerStartup _startup;
    private readonly LibVlcRuntime _mediaRuntime;
    private readonly ObservableCollection<GameSaveSlot> _saveSlots = [];
    private readonly ReadOnlyObservableCollection<GameSaveSlot> _readOnlySaveSlots;
    private GameResourceScope? _resources;
    private AvaloniaGameResources? _visualResources;
    private PlayerSession? _saveSession;
    private IGalleryResourceResolver? _galleryResources;
    private readonly GameSession _session;
    private readonly SemaphoreSlim _initialization = new(1, 1);
    private bool _disposed;
    private readonly object _disposeGate = new();
    private Task? _disposeTask;

    public SampleGameSessionService(GamePageViewModel gameplay, GamePage page, AvaloniaPlayerSettingsService settings,
        SampleLocalizationService localization, AvaloniaPlayerStartup startup, LibVlcRuntime mediaRuntime)
    {
        _session = new GameSession(CreateRunAsync);
        _gameplay = gameplay;
        _page = page;
        _settings = settings;
        _localization = localization;
        _startup = startup;
        _mediaRuntime = mediaRuntime;
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
        await _initialization.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_resources is not null) throw new InvalidOperationException("The game session is already initialized.");
            return await InitializeGameResourcesAsync(options, selectLanguage, cancellationToken);
        }
        finally { _initialization.Release(); }
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
            var catalogs = SampleCustomization.CreateContentCatalogs();
            var resources = await GameResourceScope.OpenAsync(options.GameDirectory,
                catalogs.Resources, catalogs.Gallery, cancellationToken);
            var profileDirectory = options.ProfileDirectory ?? Path.Combine(resources.GameDirectory, ".galnet");
            PlayerSession? saveSession = null;
            AvaloniaGameResources? visuals = null;
            IGalleryResourceResolver? galleryResources = null;
            try
            {
                SampleCustomization.ConfigureDecoders(resources.Assets);
                visuals = await AvaloniaGameResources.CreateAsync(resources.Assets,
                    SampleCustomization.SelectTexturePreloadsAsync, SampleCustomization.SelectEffectProgramsAsync, cancellationToken);
                saveSession = await FilePlayerSession.OpenAsync(profileDirectory, resources.Content.Gallery, cancellationToken);
                galleryResources = await AssetGalleryResourceResolver.CreateAsync(resources.Assets,
                    resources.Content.Gallery, cancellationToken, resources.MediaFiles);
            }
            catch (Exception error)
            {
                var cleanup = new ResourceCleanup();
                cleanup.Run(() => (galleryResources as IDisposable)?.Dispose());
                cleanup.Run(() => saveSession?.Dispose());
                cleanup.Run(() => visuals?.Dispose());
                cleanup.Run(resources.Dispose);
                cleanup.ThrowIfFailed(error);
                throw;
            }

            _resources = resources;
            _visualResources = visuals;
            _saveSession = saveSession;
            _galleryResources = galleryResources;
            _localization.AttachGame(resources.Content);

            foreach (var diagnostic in resources.Content.Localization.Diagnostics)
                GameLog.Logger.Warning("Skipped localization source {Path}: {Reason}", diagnostic.RelativePath, diagnostic.Message);
            var preloadFailures = visuals.SpriteFileCount - visuals.PreloadedTextures.Count;
            GameLog.Logger.Information("Preloaded {SpriteCount} sprite assets ({FailureCount} deferred to fallback)",
                visuals.SpriteFileCount, preloadFailures);
            GameLog.Logger.Information("Discovered {EffectProgramCount} effect program assets for session prewarming",
                visuals.EffectPrograms.Count);

            GalleryDataSource = saveSession.GalleryDataSource;
            GalleryResources = galleryResources;
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

    public Task PrepareContinueAsync(CancellationToken cancellationToken = default) =>
        _session.PrepareFromAsync(token => _saveSession!.LoadLatestAsync(token), cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        _disposed ? Task.CompletedTask : _session.StopAsync(cancellationToken);

    internal Task ClearPlayerStateAsync(CancellationToken cancellationToken = default) =>
        _session.ResetAsync(async token =>
        {
            var saveSession = _saveSession;
            await _settings.FlushAsync();
            if (saveSession is not null) await saveSession.ClearAsync(token);
            await OnUiAsync(() =>
            {
                _gameplay.TextSpeed = saveSession?.Settings.TextSpeed ?? _gameplay.TextSpeed;
                _gameplay.IsNvlMode = false;
                _gameplay.IsUiHidden = false;
                _gameplay.StatusMessage = "Player state cleared. Restarting...";
            });
        }, cancellationToken);

    public async Task SaveAsync(int slotIndex, CancellationToken cancellationToken = default)
    {
        await _session.WithSnapshotAsync(async (snapshot, token) =>
        {
            await _saveSession!.SaveAsync(slotIndex, snapshot, token);
            await RefreshSlotsAsync(token);
            await OnUiAsync(() => _gameplay.StatusMessage = $"Saved to slot {slotIndex}.");
            return true;
        }, cancellationToken);
    }

    public Task<int?> SaveFirstEmptyAsync(CancellationToken cancellationToken = default) =>
        _session.WithSnapshotAsync(async (snapshot, token) =>
        {
            if (!IsPlaying || _saveSession is null)
                throw new InvalidOperationException("No active game is available to save.");
            await _settings.FlushAsync();
            var slot = await _saveSession.SaveFirstEmptyAsync(snapshot, token);
            if (slot is not null)
            {
                await RefreshSlotsAsync(token);
                await OnUiAsync(() => _gameplay.StatusMessage = $"Saved to slot {slot.Value + 1}.");
            }
            return slot;
        }, cancellationToken);

    internal Task<SampleDebugSaveResult> SaveDebugSnapshotAsync(
        SampleDebugSaveKind kind,
        bool overwriteWhenAllVisibleSlotsAreOccupied,
        CancellationToken cancellationToken = default)
    {
        return _session.WithSnapshotAsync(async (currentSnapshot, token) =>
        {
            if (_saveSession is null)
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
            var snapshot = SampleDebugSnapshotFactory.Create(currentSnapshot, kind);
            await _saveSession.SaveAsync(targetSlot, snapshot, cancellationToken);
            await RefreshSlotsAsync(cancellationToken);
            await OnUiAsync(() => _gameplay.StatusMessage = $"Debug saved to slot {targetSlot}.");
            GameLog.Logger.Information("Created {DebugSaveKind} debug save in slot {SlotIndex}", kind, targetSlot);
            return SampleDebugSaveResult.Saved(targetSlot);
        }, cancellationToken);
    }

    public async Task LoadAsync(int slotIndex, CancellationToken cancellationToken = default)
    {
        await PrepareLoadAsync(slotIndex, cancellationToken);
        await BeginPreparedGameAsync(cancellationToken);
        _gameplay.StatusMessage = $"Loaded slot {slotIndex}.";
    }

    public Task PrepareLoadAsync(int slotIndex, CancellationToken cancellationToken = default) =>
        _session.PrepareFromAsync(token => _saveSession!.LoadRequiredAsync(slotIndex, token), cancellationToken);

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate) return new(_disposeTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        await _initialization.WaitAsync();
        try
        {
            var cleanup = new ResourceCleanup();
            _disposed = true;
            _gameplay.InteractionObserved -= OnInteractionObserved;
            await cleanup.RunAsync(() => _session.DisposeAsync());
            await cleanup.RunAsync(() => new(_settings.FlushAsync()));
            var saves = _saveSession;
            _saveSession = null;
            GalleryResources = null;
            GalleryDataSource = null;
            cleanup.Run(() => (_galleryResources as IDisposable)?.Dispose());
            _galleryResources = null;
            cleanup.Run(() => saves?.Dispose());
            cleanup.Run(() => _visualResources?.Dispose());
            _visualResources = null;
            var resources = _resources;
            _resources = null;
            cleanup.Run(() => resources?.Dispose());
            cleanup.ThrowIfFailed();
        }
        finally { _initialization.Release(); }
    }

    public Task BeginPreparedGameAsync(CancellationToken cancellationToken = default) =>
        _session.BeginPreparedAsync(cancellationToken);

    private Task PrepareGameAsync(GameSnapshot? snapshot, CancellationToken cancellationToken) =>
        _session.PrepareAsync(snapshot, cancellationToken);

    private Task ResetScenePresentationAsync() => OnUiAsync(_gameplay.ResetScenePresentation);

    private async Task<IGameRun> CreateRunAsync(CancellationToken cancellationToken)
    {
        var resources = _resources;
        var saveSession = _saveSession;
        if (!IsReady || resources is null || saveSession is null)
            throw new InvalidOperationException("The game session is not initialized.");

        return await SampleGameRunner.CreateAsync(
            _gameplay,
            _page,
            resources,
            _visualResources!,
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
            foreach (var slot in slots)
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
