using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.Core.Variable;
using GalNet.GameHost;
using GalNet.GameHost.Gallery;
using GalNet.Runtime.Persistence;
using GalNet.Runtime.Variables;
using GalVariable = GalNet.Core.Variable.Variable;

namespace GalNet.Avalonia.GameHost;

public partial class AvaloniaGameSession
{
    private SceneReplayHandle? _sceneReplay;
    private readonly SemaphoreSlim _replayCommands = new(1, 1);
    public async Task<IAsyncDisposable> StartSceneAsync(GalleryItemData item, GamePageViewModel model, GamePage surface,
        Action completed, CancellationToken cancellationToken)
    {
        await _replayCommands.WaitAsync(cancellationToken);
        try { return await StartSceneCoreAsync(item, model, surface, completed, cancellationToken); }
        finally { _replayCommands.Release(); }
    }
    private async Task<IAsyncDisposable> StartSceneCoreAsync(GalleryItemData item, GamePageViewModel model, GamePage surface,
        Action completed, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsReady || IsPlaying || _resources is null || _saveSession is null) throw new InvalidOperationException("Scene replay requires an idle, initialized session.");
        var registered = _saveSession.GalleryDataSource.GetTypes().SelectMany(type => type.Items)
            .SingleOrDefault(value => value.Item.Id == item.Item.Id && value.Item.TypeId == "scene" && value.Item.ResourceId == item.Item.ResourceId);
        if (registered?.IsUnlocked != true) throw new InvalidOperationException("The Gallery scene is unavailable or locked.");
        if (_sceneReplay is { } previous) await previous.DisposeAsync();
        var graph = SceneGalleryGraph.Create(_resources.Content.Graph, item.Item.ResourceId);
        var state = await SceneGalleryState.CreateAsync(_saveSession.Variables, _saveSession.Progress, _resources.Content.Gallery, cancellationToken);
        AvaloniaGameRun? run = null;
        try
        {
            run = await AvaloniaGameRun.CreateAsync(model, surface, _resources, _visualResources!, _saveSession,
                _settings, _localization, _options, _projectOptions.Audio, _mediaRuntime,
                (_, message) => { if (message is not null) model.StatusMessage = message; return Task.CompletedTask; },
                () => { completed(); return Task.CompletedTask; },
                () => OnUiAsync(model.ResetScenePresentation), OnUiAsync, cancellationToken,
                replayGraph: graph, replayVariables: state.Variables, replayProgress: state.Progress);
            var handle = new SceneReplayHandle(run, state);
            _sceneReplay = handle;
            await run.PrepareAsync(null, cancellationToken);
            await run.StartPreparedAsync(cancellationToken);
            return handle;
        }
        catch
        {
            _sceneReplay = null;
            try { if (run is not null) await run.DisposeAsync(); }
            finally { state.Dispose(); }
            throw;
        }
    }

    private async Task ReleaseSceneAsync()
    {
        await _replayCommands.WaitAsync();
        try { if (_sceneReplay is { } replay) await replay.DisposeAsync(); _sceneReplay = null; }
        finally { _replayCommands.Release(); }
    }
    private sealed class SceneReplayHandle(AvaloniaGameRun run, SceneGalleryState state) : IAsyncDisposable
    {
        private readonly object _gate = new();
        private Task? _disposal;
        public ValueTask DisposeAsync() { lock (_gate) return new(_disposal ??= DisposeCoreAsync()); }
        private async Task DisposeCoreAsync() { try { await run.DisposeAsync(); } finally { state.Dispose(); } }
    }
}
