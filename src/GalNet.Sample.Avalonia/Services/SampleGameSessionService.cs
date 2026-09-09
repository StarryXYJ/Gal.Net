using System.Collections.ObjectModel;
using Avalonia.Media;
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
using GalNet.Runtime.Engine;
using GalNet.Runtime.Handlers;
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
    private FileSaveService? _saves;
    private FileVariableService? _variables;
    private FileGameProgressService? _progress;
    private GameSettings? _settings;
    private GameEngine? _engine;
    private AvaloniaGamePageView? _pageView;
    private SampleMediaViews? _media;
    private string? _gameDirectory;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;
    private bool _disposed;

    public SampleGameSessionService(GamePageViewModel gameplay, GamePage page)
    {
        _gameplay = gameplay;
        _page = page;
        _readOnlySaveSlots = new ReadOnlyObservableCollection<GameSaveSlot>(_saveSlots);
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
            return;
        }

        try
        {
            _gameDirectory = options.GameDirectory;
            var profileDirectory = options.ProfileDirectory ?? Path.Combine(_gameDirectory, ".galnet");
            _contentProvider = new DirectoryGameContentProvider(_gameDirectory);
            _saves = new FileSaveService(profileDirectory);
            _variables = await FileVariableService.CreateAsync(new FilePlayerVariableStore(profileDirectory), cancellationToken);
            _progress = new FileGameProgressService(profileDirectory);
            _settings = new GameSettings();
            await RefreshSlotsAsync(cancellationToken);
            IsReady = true;
            StatusMessage = $"Loaded game data: {_gameDirectory}";
            _gameplay.StatusMessage = "Ready";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Unable to load game: {exception.Message}";
        }
    }

    public Task StartNewGameAsync(CancellationToken cancellationToken = default) =>
        RestartAsync(null, cancellationToken);

    public async Task ContinueAsync(CancellationToken cancellationToken = default)
    {
        var slot = _saveSlots
            .Where(candidate => !candidate.IsEmpty && !candidate.IsCorrupt)
            .OrderByDescending(candidate => candidate.Timestamp)
            .FirstOrDefault();
        if (slot is null)
        {
            StatusMessage = "There is no valid save to continue.";
            return;
        }

        await LoadAsync(slot.SlotIndex, cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return;
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
        GameSnapshot? snapshot;
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            snapshot = await _saves!.LoadAsync(slotIndex);
            if (snapshot is null)
            {
                _gameplay.StatusMessage = $"Slot {slotIndex} is empty or invalid.";
                return;
            }

            await StopCurrentRunAsync();
            DisposeEngine();
            await EnsureEngineAsync(cancellationToken);
            _engine!.RestoreFrom(snapshot);
            StartRun();
            _gameplay.StatusMessage = $"Loaded slot {slotIndex}.";
        }
        finally { _lifecycle.Release(); }
        await AwaitCurrentRunAsync(cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed) return;
        try { StopAsync().GetAwaiter().GetResult(); }
        finally
        {
            _disposed = true;
            DisposeEngine();
        }
    }

    private async Task RestartAsync(GameSnapshot? snapshot, CancellationToken cancellationToken)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            await StopCurrentRunAsync();
            DisposeEngine();
            await EnsureEngineAsync(cancellationToken);
            if (snapshot is not null) _engine!.RestoreFrom(snapshot);
            StartRun();
        }
        finally { _lifecycle.Release(); }
        await AwaitCurrentRunAsync(cancellationToken);
    }

    private void StartRun()
    {
        _runCancellation = new CancellationTokenSource();
        _runTask = RunEngineAsync(_engine!, _runCancellation.Token);
    }

    private async Task RunEngineAsync(GameEngine engine, CancellationToken cancellationToken)
    {
        try
        {
            IsPlaying = true;
            _gameplay.StatusMessage = "Playing";
            await engine.StepAsync(cancellationToken);
            _gameplay.StatusMessage = "Game flow completed.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _gameplay.StatusMessage = "Game flow was cancelled.";
        }
        catch (Exception exception)
        {
            _gameplay.StatusMessage = $"Game flow failed: {exception.Message}";
        }
        finally
        {
            IsPlaying = false;
            await RefreshSlotsAsync(CancellationToken.None);
        }
    }

    private async Task StopCurrentRunAsync()
    {
        var cancellation = _runCancellation;
        var run = _runTask;
        _runCancellation = null;
        _runTask = null;
        cancellation?.Cancel();
        if (run is not null)
        {
            try { await run; }
            catch (OperationCanceledException) { }
        }
        cancellation?.Dispose();
    }

    private Task AwaitCurrentRunAsync(CancellationToken cancellationToken) =>
        _runTask?.WaitAsync(cancellationToken) ?? Task.CompletedTask;

    private void DisposeEngine()
    {
        _pageView?.Dispose();
        _pageView = null;
        _media?.Dispose();
        _media = null;
        _engine = null;
    }

    private async Task EnsureEngineAsync(CancellationToken cancellationToken)
    {
        if (_engine is not null) return;
        if (!IsReady || _contentProvider is null || _variables is null || _progress is null || _settings is null || _gameDirectory is null)
            throw new InvalidOperationException("The game session is not initialized.");

        _pageView = new AvaloniaGamePageView(_gameplay, _page, new SampleLayerFactory(_gameDirectory));
        _media = new SampleMediaViews(_gameplay);
        var transitions = new AvaloniaTransitionView(new Dictionary<string, Func<TransitionRequest, CancellationToken, Task>>(StringComparer.OrdinalIgnoreCase)
        {
            ["black"] = (request, ct) => InvokeOnUiAsync(() => _gameplay.PlayTransitionAsync(Brushes.Black, request.Duration, ct)),
            ["white"] = (request, ct) => InvokeOnUiAsync(() => _gameplay.PlayTransitionAsync(Brushes.White, request.Duration, ct)),
            ["cross"] = (request, ct) => InvokeOnUiAsync(() => _gameplay.PlayTransitionAsync(Brushes.Black, request.Duration, ct, 0.35d))
        });
        var gameView = new CompositeGameView(_pageView, _pageView, _media, _media, transitions, new SampleEffectView(_gameplay), _pageView, _pageView);
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
    }

    private static Task InvokeOnUiAsync(Func<Task> action)
    {
        if (Dispatcher.UIThread.CheckAccess()) return action();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try { await action(); completion.TrySetResult(); }
            catch (Exception exception) { completion.TrySetException(exception); }
        });
        return completion.Task;
    }
}
