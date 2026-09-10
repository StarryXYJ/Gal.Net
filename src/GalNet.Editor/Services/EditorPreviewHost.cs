extern alias GameViewAssembly;

using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using GameViewAssembly::GalNet.Avalonia.GameView.Composition;
using GameViewAssembly::GalNet.Avalonia.GameView.Navigation;
using GameViewAssembly::GalNet.Avalonia.GameView.Page;
using GameViewAssembly::GalNet.Avalonia.GameView.Presentation;
using GameViewAssembly::GalNet.Avalonia.GameView.Services;
using GameViewAssembly::GalNet.Avalonia.GameView.ViewModels;
using GalNet.Core.Runtime;
using GalNet.Core.Services;
using GalNet.Core.Settings;
using GalNet.Core.View;
using GalNet.Runtime.Engine;
using GalNet.Runtime.Handlers;
using GalNet.Runtime.Runtime;
using GalNet.Presentation.Defaults;
using GalNet.Presentation.Abstractions.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Editor.Services;

/// <summary>Owns one isolated editor-preview DI container, game scope and engine lifetime.</summary>
public sealed class EditorPreviewHost : IAsyncDisposable
{
    private readonly ServiceProvider _services;
    private readonly AsyncServiceScope _scope;
    private readonly EditorPreviewSessionService _session;
    private bool _disposed;

    public GameShell Shell { get; }
    public IGameRuntime? Runtime => _session.Runtime;

    public EditorPreviewHost(EditorPreviewContext context)
    {
        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddAvaloniaGameViewPages();
        services.AddScoped<EditorPreviewSessionService>();
        services.AddScoped<IGameSessionService>(provider => provider.GetRequiredService<EditorPreviewSessionService>());
        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        _scope = _services.CreateAsyncScope();

        var provider = _scope.ServiceProvider;
        provider.GetRequiredService<IGameNavigationService>().ResetTo<GamePageViewModel>();
        Shell = provider.GetRequiredService<GameShell>();
        _session = provider.GetRequiredService<EditorPreviewSessionService>();
    }

    public Task StartAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _session.StartNewGameAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _session.StopAsync();
        await _scope.DisposeAsync();
        await _services.DisposeAsync();
    }
}

public sealed record EditorPreviewContext(
    string Title,
    string AssetRoot,
    IGameContentProvider Content,
    IVariableService Variables,
    ISaveService Saves,
    IGameProgressService Progress,
    Action<IGameRuntime> RuntimeCreated,
    Action GameStarted,
    Action GameEnded,
    Action<Exception> GameFailed);

internal sealed partial class EditorPreviewSessionService : ObservableObject, IGameSessionService, IDisposable
{
    private readonly EditorPreviewContext _context;
    private readonly GamePageViewModel _gameplay;
    private readonly GamePage _page;
    private readonly ObservableCollection<GameSaveSlot> _slots = [];
    private readonly ReadOnlyObservableCollection<GameSaveSlot> _readOnlySlots;
    private readonly NullGameView _fallback = new();
    private AvaloniaGamePageView? _pageView;
    private GameEngine? _engine;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly GameRunCoordinator _run = new();
    private bool _disposed;

    public EditorPreviewSessionService(EditorPreviewContext context, GamePageViewModel gameplay, GamePage page)
    {
        _context = context;
        _gameplay = gameplay;
        _page = page;
        _readOnlySlots = new(_slots);
    }

    public IGameRuntime? Runtime => _engine?.Runtime;
    public string GameTitle => _context.Title;
    public bool IsReady => true;
    public ReadOnlyObservableCollection<GameSaveSlot> SaveSlots => _readOnlySlots;
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private bool _canContinue;

    public Task StartNewGameAsync(CancellationToken cancellationToken = default) =>
        RestartAsync(null, cancellationToken);

    public async Task ContinueAsync(CancellationToken cancellationToken = default)
    {
        var slot = _slots
            .Where(candidate => !candidate.IsEmpty && !candidate.IsCorrupt)
            .OrderByDescending(candidate => candidate.Timestamp)
            .FirstOrDefault();
        if (slot is not null) await LoadAsync(slot.SlotIndex, cancellationToken);
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
            await _context.Saves.SaveAsync(slotIndex, new SaveRequest { Snapshot = _engine!.CreateSaveData() }, cancellationToken);
            await RefreshSlotsAsync(cancellationToken);
        }
        finally { _lifecycle.Release(); }
    }

    public async Task LoadAsync(int slotIndex, CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            var snapshot = await _context.Saves.LoadAsync(slotIndex);
            if (snapshot is null)
            {
                StatusMessage = $"Slot {slotIndex} is empty or invalid.";
                return;
            }

            await StopCurrentRunAsync();
            DisposeEngine();
            await EnsureEngineAsync(cancellationToken);
            _engine!.RestoreFrom(snapshot);
            StartRun();
            StatusMessage = $"Loaded slot {slotIndex}.";
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
        _run.Start(cancellationToken => RunEngineAsync(_engine!, cancellationToken));
    }

    private async Task RunEngineAsync(GameEngine engine, CancellationToken cancellationToken)
    {
        try
        {
            IsPlaying = true;
            _context.GameStarted();
            StatusMessage = "Running";
            await engine.StepAsync(cancellationToken);
            StatusMessage = "Ready";
            _context.GameEnded();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusMessage = "Stopped";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Preview failed: {exception.Message}";
            _context.GameFailed(exception);
        }
        finally
        {
            IsPlaying = false;
            await RefreshSlotsAsync(CancellationToken.None);
        }
    }

    private async Task StopCurrentRunAsync()
    {
        await _run.StopAsync();
    }

    private Task AwaitCurrentRunAsync(CancellationToken cancellationToken) =>
        _run.WaitAsync(cancellationToken);

    private void DisposeEngine()
    {
        _pageView?.Dispose();
        _pageView = null;
        _engine = null;
    }

    private async Task EnsureEngineAsync(CancellationToken cancellationToken)
    {
        if (_engine is not null) return;
        _pageView = new AvaloniaGamePageView(_gameplay, _page, new EditorPreviewLayerFactory(_context.AssetRoot));
        var view = new CompositeGameView(
            _pageView, _pageView, _fallback, _fallback, _fallback, _fallback, _pageView, _pageView);
        var content = await _context.Content.LoadAsync(cancellationToken);
        var runtime = new GameRuntime(null, content.Graph.RootNodeId, new SettingsContainer(), _context.Variables);
        _engine = new GameEngine(
            content.Graph, runtime, view, EntryHandlerRegistry.CreateDefault(_context.Progress), _context.Progress);
        _context.RuntimeCreated(runtime);
        await RefreshSlotsAsync(cancellationToken);
    }

    private async Task RefreshSlotsAsync(CancellationToken cancellationToken)
    {
        var slots = await _context.Saves.ListSlotsAsync(cancellationToken);
        _slots.Clear();
        foreach (var slot in slots)
            _slots.Add(new(slot.SlotIndex, slot.Timestamp,
                slot.IsCorrupt ? "Corrupt save" : slot.Description ?? (slot.Timestamp == default ? "Empty" : "Saved game"),
                slot.Timestamp == default && !slot.IsCorrupt, slot.IsCorrupt));
        CanContinue = _slots.Any(slot => !slot.IsEmpty && !slot.IsCorrupt);
    }
}

internal sealed class EditorPreviewLayerFactory(string assetRoot) : IGamePageLayerFactory
{
    public Control CreateLayer(string assetId)
    {
        var path = Path.IsPathRooted(assetId) ? assetId : Path.Combine(assetRoot, assetId);
        try
        {
            if (File.Exists(path))
                return new Image { Source = new Bitmap(path), Stretch = Stretch.UniformToFill };
        }
        catch
        {
            // Preview intentionally falls back to a visible placeholder.
        }

        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#551B1C27")),
            Child = new TextBlock { Text = assetId, Margin = new Thickness(12), TextWrapping = TextWrapping.Wrap }
        };
    }
}
