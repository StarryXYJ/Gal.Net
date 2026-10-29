using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Avalonia.GameHost.Settings;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.Core.Runtime;
using GalNet.GameHost;
using GalNet.Media.LibVlc;
using GalNet.Runtime.Logging;
using GalNet.Storage.FileSystem;

namespace GalNet.Avalonia.GameHost;

/// <summary>Default Avalonia session adapter. Neutral GameSession owns command coordination.</summary>
public partial class AvaloniaGameSession : ObservableObject, ISavePreviewSource, IGameSessionService, IPreparedGameSessionService, IParameterizedGameSessionService, IFirstEmptySlotSession, IGameGallerySession, IAsyncDisposable
{
    private readonly AvaloniaGameHostOptions _options;
    private readonly GamePageViewModel _gameplay;
    private readonly GamePage _page;
    private readonly IAvaloniaPlayerSettings _settings;
    private readonly IPlayerLocalization _localization;
    private readonly IPlayerStartup _startup;
    private readonly LibVlcRuntime _mediaRuntime;
    private readonly ObservableCollection<GameSaveSlot> _saveSlots = [];
    private readonly ReadOnlyObservableCollection<GameSaveSlot> _readOnlySaveSlots;
    private GameResourceScope? _resources;
    private AvaloniaProjectOptions _projectOptions = new();
    private AvaloniaGameResources? _visualResources;
    private PlayerSession? _saveSession;
    private IGalleryResourceResolver? _galleryResources;
    private readonly GameSession _session;
    private IDisposable? _playerBinding;
    private readonly SemaphoreSlim _initialization = new(1, 1);
    private bool _disposed;
    private readonly object _disposeGate = new();
    private Task? _disposeTask;

    public AvaloniaGameSession(AvaloniaGameHostOptions options, GamePageViewModel gameplay, GamePage page, IAvaloniaPlayerSettings settings,
        IPlayerLocalization localization, IPlayerStartup startup, LibVlcRuntime mediaRuntime)
    {
        _options = options;
        GameTitle = options.GameTitle;
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
    [ObservableProperty] private string _gameTitle = "GalNet";
    [ObservableProperty] private string _statusMessage = "Select a game directory to start.";
    [ObservableProperty] private bool _isReady;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private bool _canContinue;

    public async Task<bool> InitializeAsync(string? gameDirectory, string? profileDirectory,
        Func<string, CancellationToken, Task<string?>> selectLanguage, CancellationToken cancellationToken = default)
    {
        await _initialization.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_resources is not null) throw new InvalidOperationException("The game session is already initialized.");
            return await InitializeGameResourcesAsync(gameDirectory, profileDirectory, selectLanguage, cancellationToken);
        }
        finally { _initialization.Release(); }
    }

    private async Task<bool> InitializeGameResourcesAsync(string? gameDirectory, string? profileDirectory,
        Func<string, CancellationToken, Task<string?>> selectLanguage, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory))
        {
            StatusMessage = _options.NoGameMessage;
            GameLog.Logger.Warning("Session initialized without a game directory");
            return true;
        }

        try
        {
            var catalogs = _options.CreateContentCatalogs();
            var resources = await GameResourceScope.OpenAsync(gameDirectory,
                catalogs.Resources, catalogs.Gallery, _options.Content, cancellationToken);
            AvaloniaProjectOptions projectOptions;
            profileDirectory ??= Path.Combine(resources.GameDirectory, ".galnet");
            PlayerSession? saveSession = null;
            AvaloniaGameResources? visuals = null;
            IGalleryResourceResolver? galleryResources = null;
            try
            {
                projectOptions = _options.SelectProjectOptions(resources.Content.Configuration);
                _options.ConfigureDecoders(resources.Assets);
                visuals = await AvaloniaGameResources.CreateAsync(resources.Assets,
                    _options.SelectTexturePreloads, _options.SelectEffectPrograms, cancellationToken);
                saveSession = await FilePlayerSession.OpenAsync(profileDirectory, resources.Content.Gallery,
                    projectOptions.SaveSlotCount, cancellationToken);
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
            _projectOptions = projectOptions;
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
                    projectOptions.DefaultCulture, selectLanguage, cancellationToken))
                throw new OperationCanceledException("Language selection was cancelled.", cancellationToken);
            await _settings.AttachAsync(saveSession.Variables,
                projectOptions.Audio, _options.ShowTrackAudioControls, cancellationToken);
            await _settings.RestoreDisplayAsync(cancellationToken);

            _playerBinding = _options.AttachPlayer?.Invoke(new(saveSession, resources.Content, profileDirectory));
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

    public async Task StartNewGameAsync(IReadOnlyDictionary<string, object> initialSaveVariables, CancellationToken cancellationToken = default)
    {
        await PrepareNewGameAsync(initialSaveVariables, cancellationToken);
        await BeginPreparedGameAsync(cancellationToken);
    }

    public Task PrepareNewGameAsync(IReadOnlyDictionary<string, object> initialSaveVariables, CancellationToken cancellationToken = default) =>
        _session.PrepareNewAsync(initialSaveVariables, cancellationToken);

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

    public Task ClearPlayerStateAsync(CancellationToken cancellationToken = default) =>
        _session.ResetAsync(async token =>
        {
            var saveSession = _saveSession;
            await _settings.FlushAsync();
            if (saveSession is not null) await saveSession.ClearAsync(token);
            await AvaloniaUiDispatcher.Instance.InvokeAsync(async () =>
            {
                if (saveSession is not null)
                    await _settings.AttachAsync(saveSession.Variables,
                        _projectOptions.Audio,
                        _options.ShowTrackAudioControls, token);
                _gameplay.TextSpeed = _settings.TextSpeed;
                _gameplay.IsNvlMode = false;
                _gameplay.IsUiHidden = false;
                _gameplay.StatusMessage = "Player state cleared. Restarting...";
            });
        }, cancellationToken);

    public Func<byte[]?>? CaptureSavePreview { get; set; }

    public async Task SaveAsync(int slotIndex, CancellationToken cancellationToken = default)
    {
        var preview = CaptureSavePreview?.Invoke();
        await _session.WithSnapshotAsync(async (snapshot, token) =>
        {
            await _saveSession!.SaveAsync(slotIndex, snapshot, preview, token);
            await RefreshSlotsAsync(token);
            await OnUiAsync(() => _gameplay.StatusMessage = $"Saved to slot {slotIndex}.");
            return true;
        }, cancellationToken);
    }

    public Task<int?> SaveFirstEmptyAsync(CancellationToken cancellationToken = default)
    {
        var preview = CaptureSavePreview?.Invoke();
        return _session.WithSnapshotAsync(async (snapshot, token) =>
        {
            if (!IsPlaying || _saveSession is null)
                throw new InvalidOperationException("No active game is available to save.");
            await _settings.FlushAsync();
            var slot = await _saveSession.SaveFirstEmptyAsync(snapshot, preview, token);
            if (slot is not null)
            {
                await RefreshSlotsAsync(token);
                await OnUiAsync(() => _gameplay.StatusMessage = $"Saved to slot {slot.Value + 1}.");
            }
            return slot;
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
        GC.SuppressFinalize(this);
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
            await cleanup.RunAsync(() => ResourceCleanup.DisposeAsync(_playerBinding));
            _playerBinding = null;
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

        return await AvaloniaGameRun.CreateAsync(
            _gameplay,
            _page,
            resources,
            _visualResources!,
            saveSession,
            _settings,
            _localization,
            _options,
            _projectOptions.Audio,
            _mediaRuntime.Instance,
            UpdateRunnerStateAsync,
            () => RefreshSlotsAsync(CancellationToken.None),
            ResetScenePresentationAsync,
            OnUiAsync,
            cancellationToken);
    }

    protected async Task RefreshSlotsAsync(CancellationToken cancellationToken)
    {
        if (_saveSession is null) return;
        var slots = await _saveSession.ListSlotsAsync(cancellationToken);
        var previews = new Dictionary<int, byte[]>();
        foreach (var slot in slots)
        {
            if (slot.PreviewImage is not { } path) continue;
            try { previews[slot.SlotIndex] = await File.ReadAllBytesAsync(path, cancellationToken); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
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
                    slot.IsCorrupt) { PreviewImage = previews.GetValueOrDefault(slot.SlotIndex) });
            }
            CanContinue = _saveSlots.Any(slot => !slot.IsEmpty && !slot.IsCorrupt);
        });
    }

    /// <summary>Runs a project-specific save action under the same command gate as normal saves and run replacement.</summary>
    protected Task<T> WithSnapshotAsync<T>(Func<GameSnapshot, PlayerSession, CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default) =>
        _session.WithSnapshotAsync((snapshot, token) => action(snapshot,
            _saveSession ?? throw new InvalidOperationException("The player session is not initialized."), token), cancellationToken);

    protected Task SetGameplayStatusAsync(string message) => OnUiAsync(() => _gameplay.StatusMessage = message);

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
