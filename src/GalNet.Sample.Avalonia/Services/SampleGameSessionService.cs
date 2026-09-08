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

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureEngineAsync(cancellationToken);
            _gameplay.StatusMessage = "Playing";
            await _engine!.StepAsync(cancellationToken);
            _gameplay.StatusMessage = "Game flow completed.";
            await RefreshSlotsAsync(cancellationToken);
        }
        catch (OperationCanceledException) { _gameplay.StatusMessage = "Game flow was cancelled."; }
        catch (Exception exception) { _gameplay.StatusMessage = $"Game flow failed: {exception.Message}"; }
    }

    public async Task SaveAsync(int slotIndex, CancellationToken cancellationToken = default)
    {
        await EnsureEngineAsync(cancellationToken);
        await _saves!.SaveAsync(slotIndex, _engine!.CreateSaveData());
        _gameplay.StatusMessage = $"Saved to slot {slotIndex}.";
        await RefreshSlotsAsync(cancellationToken);
    }

    public async Task LoadAsync(int slotIndex, CancellationToken cancellationToken = default)
    {
        await EnsureEngineAsync(cancellationToken);
        var snapshot = await _saves!.LoadAsync(slotIndex);
        if (snapshot is null)
        {
            _gameplay.StatusMessage = $"Slot {slotIndex} is empty or invalid.";
            return;
        }

        _engine!.RestoreFrom(snapshot);
        _gameplay.StatusMessage = $"Loaded slot {slotIndex}.";
        await StartAsync(cancellationToken);
    }

    public void Dispose()
    {
        _pageView?.Dispose();
        _media?.Dispose();
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
                slot.Timestamp == default ? string.Empty : slot.Timestamp.ToString("g"),
                slot.IsCorrupt ? "Corrupt save" : slot.Timestamp == default ? "Empty" : "Saved game",
                slot.Timestamp == default && !slot.IsCorrupt,
                slot.IsCorrupt));
        }
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
