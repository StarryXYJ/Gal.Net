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
using GalNet.Rendering.Scene;
using GalNet.Core.Assets;
using GalNet.Core.Gallery;
using GalNet.Core.Runtime;
using GalNet.Core.Services;
using GalNet.Core.Settings;
using GalNet.Core.View;
using GalNet.Runtime.Engine;
using GalNet.Runtime.Runtime;
using GalNet.Presentation.Defaults;
using GalNet.Presentation.Abstractions.Runtime;
using GalNet.Primitives.Builtins;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

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
    IAssetManager Assets,
    IGameContentProvider Content,
    IVariableService Variables,
    ISaveService Saves,
    IGameProgressService Progress,
    Action<IGameRuntime> RuntimeCreated,
    Action GameStarted,
    Action GameEnded,
    Action<Exception> GameFailed);

internal sealed partial class EditorPreviewSessionService : ObservableObject, IGameSessionService, IGameGallerySession, IDisposable
{
    private readonly EditorPreviewContext _context;
    private readonly GamePageViewModel _gameplay;
    private readonly GamePage _page;
    private readonly ObservableCollection<GameSaveSlot> _slots = [];
    private readonly ReadOnlyObservableCollection<GameSaveSlot> _readOnlySlots;
    private readonly NullGameView _fallback = new();
    private AvaloniaGamePageView? _pageView;
    private EditorPreviewLayerFactory? _layers;
    private AvaloniaEffectRuntime? _effects;
    private GameEngine? _engine;
    private readonly Dictionary<string, AssetHandle<SceneTexture>> _preloadedTextures = new(StringComparer.OrdinalIgnoreCase);
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
    public IGalleryDataSource? GalleryDataSource { get; private set; }
    public IGalleryResourceResolver? GalleryResources { get; private set; }
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
            await engine.AdvanceAsync(cancellationToken);
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
        (GalleryResources as IDisposable)?.Dispose();
        GalleryResources = null;
        if (_pageView is not null) _pageView.AdvanceRequested -= OnAdvanceRequested;
        _engine?.Dispose();
        _pageView?.Dispose();
        _layers?.Dispose();
        _effects?.Dispose();
        foreach (var handle in _preloadedTextures.Values) handle.Dispose();
        _preloadedTextures.Clear();
        _pageView = null;
        _layers = null;
        _effects = null;
        _engine = null;
    }

    private async Task EnsureEngineAsync(CancellationToken cancellationToken)
    {
        if (_engine is not null) return;
        var content = await _context.Content.LoadAsync(cancellationToken);
        _context.Assets.RegisterDecoder<SceneTexture>("sprite", new SceneTextureAssetDecoder());
        var spriteFiles = await _context.Assets.GetFilesAsync("sprite", cancellationToken);
        var textures = await Task.WhenAll(spriteFiles.Select(file => _context.Assets.AcquireAsync<SceneTexture>(file.Id, cancellationToken)));
        foreach (var handle in textures.OfType<AssetHandle<SceneTexture>>()) _preloadedTextures.Add(handle.AssetId, handle);
        _context.Variables.ConfigureSystemVariables(GalleryUnlockVariable.CreateDefinitions(content.Gallery));
        GalleryDataSource = new GalleryDataSource(content.Gallery, _context.Variables);
        GalleryResources = await AssetGalleryResourceResolver.CreateAsync(_context.Assets, content.Gallery, cancellationToken);
        OnPropertyChanged(nameof(GalleryDataSource));
        OnPropertyChanged(nameof(GalleryResources));
        _layers = new EditorPreviewLayerFactory(_preloadedTextures);
        _pageView = new AvaloniaGamePageView(_gameplay, _page, _layers);
        _effects = new AvaloniaEffectRuntime(_gameplay);
        var view = new CompositeGameView(BuiltinEntryModules.CreateRecommended(
            _pageView, _pageView, _pageView, _effects, content.Gallery, _pageView));
        var runtime = new GameRuntime(null, content.Graph.RootNodeId, new SettingsContainer(), _context.Variables);
        _engine = new GameEngine(content.Graph, runtime, view, _context.Progress, _pageView);
        _pageView.AdvanceRequested += OnAdvanceRequested;
        _context.RuntimeCreated(runtime);
        await RefreshSlotsAsync(cancellationToken);
    }

    private void OnAdvanceRequested()
    {
        if (_engine is { } engine)
            _ = AdvanceEngineAsync(engine);
    }

    private static async Task AdvanceEngineAsync(GameEngine engine)
    {
        try { await engine.AdvanceAsync(); }
        catch (Exception exception) { Log.Error(exception, "Preview advance failed"); }
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

internal sealed class EditorPreviewLayerFactory(IReadOnlyDictionary<string, AssetHandle<SceneTexture>> textures) : IGamePageLayerFactory, IDisposable
{
    private readonly IReadOnlyDictionary<string, AssetHandle<SceneTexture>> _texturesById = textures;
    private readonly Dictionary<string, SceneTexture> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<SceneTexture> _fallbacks = [];

    public SceneTexture ResolveTexture(string assetId)
    {
        if (_textures.TryGetValue(assetId, out var texture)) return texture;
        texture = LoadTexture(assetId);
        _textures.Add(assetId, texture);
        return texture;
    }

    private SceneTexture LoadTexture(string assetId)
    {
        try
        {
            if (_texturesById.TryGetValue(assetId, out var handle) && !handle.IsReleased)
                return handle.Value;
        }
        catch (Exception exception)
        {
            ReportMissing(assetId, exception);
            return CreateFallback();
        }

        ReportMissing(assetId, null);
        return CreateFallback();
    }

    private SceneTexture CreateFallback()
    {
        var fallback = new SceneTexture(LayerImageFallback.MissingImage);
        _fallbacks.Add(fallback);
        return fallback;
    }

    public void Dispose()
    {
        foreach (var fallback in _fallbacks) fallback.Dispose();
        _fallbacks.Clear();
        _textures.Clear();
    }

    private static void ReportMissing(string assetId, Exception? exception)
    {
        if (!LayerImageFallback.ShouldReport(assetId)) return;
        if (exception is null)
            Log.Warning("Layer asset was not found; rendering built-in fallback: {AssetId}", assetId);
        else
            Log.Warning(exception, "Layer asset could not be read; rendering built-in fallback: {AssetId}", assetId);
    }
}
