using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Avalonia.GameView;
using GalNet.Avalonia.GameView.Page;
using GalNet.Core.Runtime;
using GalNet.Core.Settings;
using GalNet.Core.View;
using GalNet.Runtime.Engine;
using GalNet.Runtime.Handlers;
using GalNet.Runtime.Runtime;
using GalNet.Sample.Avalonia.Presentation;
using GalNet.Storage.FileSystem;

namespace GalNet.Sample.Avalonia.ViewModels;

/// <summary>
/// Composition root for the reference player. The gameplay page itself is owned by
/// GalNet.Avalonia.GameView and can therefore also be hosted by the editor preview.
/// </summary>
public sealed partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private DirectoryGameContentProvider? _contentProvider;
    private FileSaveService? _saves;
    private FileVariableService? _variables;
    private FileGameProgressService? _progress;
    private GameEngine? _engine;
    private AvaloniaGamePageView? _pageView;
    private SampleMediaViews? _media;
    private GameSettings? _settings;

    public GamePageViewModel Page { get; } = new();

    [ObservableProperty] private string _statusMessage = "Pass a published game directory when launching the sample.";
    [ObservableProperty] private bool _isReady;
    [ObservableProperty] private bool _isGameStarted;
    [ObservableProperty] private bool _isSettingsVisible;
    [ObservableProperty] private bool _isGalleryVisible;

    internal async Task InitializeAsync(GameLaunchOptions options, GamePage gamePage)
    {
        if (string.IsNullOrWhiteSpace(options.GameDirectory))
        {
            StatusMessage = "Usage: GalNet.Sample.Avalonia <game-data-directory> [--profile <directory>]";
            return;
        }

        try
        {
            var gameDirectory = options.GameDirectory;
            var profileDirectory = options.ProfileDirectory ?? Path.Combine(gameDirectory, ".galnet");
            _contentProvider = new DirectoryGameContentProvider(gameDirectory);
            _saves = new FileSaveService(profileDirectory);
            _variables = await FileVariableService.CreateAsync(new FilePlayerVariableStore(profileDirectory));
            _progress = new FileGameProgressService(profileDirectory);
            _settings = new GameSettings();

            _pageView = new AvaloniaGamePageView(Page, gamePage, new SampleLayerFactory(gameDirectory));
            _media = new SampleMediaViews(Page, gameDirectory);
            Page.SaveRequested += () => _ = SaveToSlotAsync(0);
            Page.LoadSlotRequested += slotIndex => _ = LoadFromSlotAsync(slotIndex);
            Page.SettingsRequested += () => { Page.IsMenuVisible = false; IsSettingsVisible = true; };
            Page.GalleryRequested += () => { Page.IsMenuVisible = false; IsGalleryVisible = true; };

            var transitions = new AvaloniaTransitionView(new Dictionary<string, Func<TransitionRequest, CancellationToken, Task>>(StringComparer.OrdinalIgnoreCase)
            {
                ["black"] = (request, ct) => InvokeOnUiAsync(() => Page.PlayTransitionAsync(Brushes.Black, request.Duration, ct)),
                ["white"] = (request, ct) => InvokeOnUiAsync(() => Page.PlayTransitionAsync(Brushes.White, request.Duration, ct)),
                ["cross"] = (request, ct) => InvokeOnUiAsync(() => Page.PlayTransitionAsync(Brushes.Black, request.Duration, ct, 0.35d))
            });
            var gameView = new CompositeGameView(_pageView, _pageView, _media, _media, transitions, new SampleEffectView(Page), _pageView, _pageView);

            CreateEngine(gameView);
            await RefreshSlotsAsync();
            IsReady = true;
            StatusMessage = $"Loaded game data: {gameDirectory}";
            Page.StatusMessage = "Ready";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Unable to load game: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task StartGameAsync()
    {
        if (_engine is null)
        {
            StatusMessage = "No game data is available.";
            return;
        }

        try
        {
            IsGameStarted = true;
            Page.IsMenuVisible = false;
            IsSettingsVisible = false;
            IsGalleryVisible = false;
            Page.StatusMessage = "Playing";
            await _engine.StepAsync();
            Page.StatusMessage = "Game flow completed.";
            await RefreshSlotsAsync();
        }
        catch (OperationCanceledException)
        {
            Page.StatusMessage = "Game flow was cancelled.";
        }
        catch (Exception exception)
        {
            Page.StatusMessage = $"Game flow failed: {exception.Message}";
        }
    }

    private async Task SaveToSlotAsync(int slotIndex)
    {
        if (_engine is null || _saves is null) return;
        await _saves.SaveAsync(slotIndex, _engine.CreateSaveData());
        Page.StatusMessage = $"Saved to slot {slotIndex}.";
        await RefreshSlotsAsync();
    }

    private async Task LoadFromSlotAsync(int slotIndex)
    {
        if (_engine is null || _saves is null) return;
        var snapshot = await _saves.LoadAsync(slotIndex);
        if (snapshot is null)
        {
            Page.StatusMessage = $"Slot {slotIndex} is empty or invalid.";
            return;
        }

        _engine.RestoreFrom(snapshot);
        IsGameStarted = true;
        Page.IsMenuVisible = false;
        Page.StatusMessage = $"Loaded slot {slotIndex}.";
        await StartGameAsync();
    }

    [RelayCommand] private void CloseSettings() => IsSettingsVisible = false;
    [RelayCommand] private void CloseGallery() => IsGalleryVisible = false;

    private void CreateEngine(IGameView gameView)
    {
        if (_contentProvider is null || _settings is null || _variables is null || _progress is null)
            throw new InvalidOperationException("The player composition root is incomplete.");

        var content = _contentProvider.LoadAsync().GetAwaiter().GetResult();
        var settings = new SettingsContainer();
        settings.Set(_settings);
        var runtime = new GameRuntime(null, content.Graph.RootNodeId, settings, _variables);
        _engine = new GameEngine(content.Graph, runtime, gameView, EntryHandlerRegistry.CreateDefault(_progress), _progress);
    }

    private async Task RefreshSlotsAsync()
    {
        if (_saves is null) return;
        var slots = await _saves.ListSlotsAsync();
        Page.SaveSlots.Clear();
        foreach (var slot in slots.Take(12))
        {
            Page.SaveSlots.Add(new GamePageSaveSlot(
                slot.SlotIndex,
                slot.Timestamp == default ? string.Empty : slot.Timestamp.ToString("g"),
                slot.IsCorrupt ? "Corrupt save" : slot.Timestamp == default ? "Empty" : "Saved game",
                slot.Timestamp == default && !slot.IsCorrupt,
                slot.IsCorrupt));
        }
    }

    public void Dispose()
    {
        _pageView?.Dispose();
        _media?.Dispose();
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
