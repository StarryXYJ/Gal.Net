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
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Editor.Services;

/// <summary>Owns one isolated editor-preview DI container, game scope and engine lifetime.</summary>
public sealed class EditorPreviewHost : IAsyncDisposable
{
    private readonly ServiceProvider _services;
    private readonly AsyncServiceScope _scope;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly EditorPreviewSessionService _session;
    private Task? _runTask;
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
        return _runTask ??= _session.StartAsync(_lifetime.Token);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _lifetime.CancelAsync();
        if (_runTask is not null)
        {
            try { await _runTask; }
            catch (OperationCanceledException) { }
        }
        await _scope.DisposeAsync();
        await _services.DisposeAsync();
        _lifetime.Dispose();
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

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureEngineAsync(cancellationToken);
            _context.GameStarted();
            StatusMessage = "Running";
            await _engine!.StepAsync(cancellationToken);
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
    }

    public async Task SaveAsync(int slotIndex, CancellationToken cancellationToken = default)
    {
        await EnsureEngineAsync(cancellationToken);
        await _context.Saves.SaveAsync(slotIndex, new SaveRequest { Snapshot = _engine!.CreateSaveData() }, cancellationToken);
        await RefreshSlotsAsync(cancellationToken);
    }

    public async Task LoadAsync(int slotIndex, CancellationToken cancellationToken = default)
    {
        await EnsureEngineAsync(cancellationToken);
        if (await _context.Saves.LoadAsync(slotIndex) is { } snapshot)
            _engine!.RestoreFrom(snapshot);
    }

    public void Dispose() => _pageView?.Dispose();

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
            _slots.Add(new(slot.SlotIndex, slot.Timestamp == default ? string.Empty : slot.Timestamp.ToString("g"),
                slot.IsCorrupt ? "Corrupt save" : slot.Description ?? (slot.Timestamp == default ? "Empty" : "Saved game"),
                slot.Timestamp == default && !slot.IsCorrupt, slot.IsCorrupt));
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
