using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.Services;
using GalNet.Contracts.Builtins.Gallery;

namespace GalNet.Avalonia.GameView.Presentation;

/// <summary>Owns an independent replay presentation and drains pending startup before releasing its run.</summary>
public sealed partial class GalleryScenePlayback : ObservableObject, IAsyncDisposable, IDisposable
{
    private readonly IGameSessionService _session;
    private IAsyncDisposable? _run;
    private Task<IAsyncDisposable>? _starting;
    private int _generation;
    private CancellationTokenSource? _lifetime;
    private bool _closing;
    private Task? _disposal;
    public GamePresentationState Gameplay { get; }
    public GamePage Surface { get; }
    [ObservableProperty] private string? _errorMessage;

    public GalleryScenePlayback(IGameSessionService session, GamePage? surface = null)
    {
        _session = session;
        Gameplay = surface?.Presentation ?? new();
        Gameplay.IsGameUiReady = true;
        Gameplay.IsGalleryReplay = true;
        Surface = surface ?? new();
        Surface.Presentation = Gameplay;
        Gameplay.ReturnToTitleRequested += RequestClose;
    }
    public async Task ActivateAsync(GalleryItemData item, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposal is not null, this);
        await ReleaseAsync(); _closing = false; ErrorMessage = null;
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _lifetime = lifetime; var generation = ++_generation;
        try
        {
            if (!item.IsUnlocked) throw new InvalidOperationException("This Gallery scene is locked.");
            if (_session is not ISceneGallerySession scenes) throw new NotSupportedException("The host does not support scene Gallery playback.");
            var starting = scenes.StartSceneAsync(item, Gameplay, Surface,
                () => { if (generation == _generation) RequestClose(); }, lifetime.Token);
            _starting = starting;
            var run = await starting;
            if (generation != _generation) return; // The releasing generation owns the pending run.
            _starting = null;
            if (lifetime.IsCancellationRequested) { await run.DisposeAsync(); return; }
            _run = run;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { ErrorMessage = error.Message; }
    }
    public event Action? CloseRequested;
    private void RequestClose() => Dispatcher.UIThread.Post(() => CloseRequested?.Invoke());
    public async Task<bool> CloseAsync()
    {
        if (_closing) return false; _closing = true;
        try { await ReleaseAsync(); return true; }
        catch (Exception error) { ErrorMessage = error.Message; _closing = false; return false; }
    }
    private async Task ReleaseAsync()
    {
        ++_generation;
        var lifetime = _lifetime; _lifetime = null; lifetime?.Cancel();
        var run = _run; _run = null;
        var starting = _starting; _starting = null;
        try
        {
            if (run is null && starting is not null)
            {
                try { run = await starting; }
                catch (Exception) { /* Activation reports startup errors; the host releases failed runs. */ }
            }
            if (run is not null) await run.DisposeAsync();
        }
        finally { lifetime?.Dispose(); Gameplay.ResetScenePresentation(); }
    }
    private Task DisposeCoreAsync() { Gameplay.ReturnToTitleRequested -= RequestClose; return ReleaseAsync(); }
    public ValueTask DisposeAsync() => new(_disposal ??= DisposeCoreAsync());
    public void Dispose()
    {
        var disposal = _disposal ??= DisposeCoreAsync();
        if (!disposal.IsCompletedSuccessfully) _ = ObserveDisposalAsync(disposal);
    }
    private static async Task ObserveDisposalAsync(Task task)
    {
        try { await task; } catch (Exception error) { Serilog.Log.Error(error, "Scene Gallery cleanup failed"); }
    }
}
