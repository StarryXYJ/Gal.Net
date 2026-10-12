using GalNet.Core.Primitives;
using GalNet.Core.Runtime;
using GalNet.Core.Scene;
using GalNet.Presentation.Abstractions.View;
using GalNet.Runtime.Logging;

namespace GalNet.Primitives.Builtins;

public sealed class VideoPrimitiveInstance(
    IGameRuntime runtime,
    IVideoPresenter? presenter,
    VideoRenderRequest request,
    bool blocking,
    string? batchId,
    CancellationToken cancellationToken) : PrimitiveInstance(batchId)
{
    private bool _catchUpDetached;
    private Task _presentationTask = Task.CompletedTask;
    public override Task PresentationCompletion => _presentationTask;
    public override bool IsBlocking => blocking;
    public override bool IsSkippable => blocking && !IsCompleted;

    protected override void OnDispatch()
    {
        BuiltinRuntimeActions.ShowLayer(runtime, null, request.Layer);
        var layer = runtime.SceneInstances.GetOrAdd(request.Layer.HandleId, id => new Layer { Id = id });
        layer.IsVideo = true;
        layer.VideoPlaybackId = request.PlaybackId;
        _presentationTask = ObserveAsync();
    }

    protected override void OnSkip() => presenter?.AdvanceVideo(request.PlaybackId);

    protected override void OnPlaybackModeChanged(PrimitiveInvocationMode mode)
    {
        if (mode == PrimitiveInvocationMode.Normal || (!blocking && mode != PrimitiveInvocationMode.CatchUp)) return;
        _catchUpDetached = !blocking;
        if (runtime.SceneInstances.TryGet<Layer>(request.Layer.HandleId, out var layer) &&
            layer.VideoPlaybackId == request.PlaybackId)
        {
            presenter?.StopVideo(request.Layer.HandleId);
            if (blocking) runtime.SceneInstances.Remove<Layer>(request.Layer.HandleId, out _);
        }
        TryComplete();
    }

    protected override void OnCatchUpDispatch()
    {
        if (!blocking)
        {
            BuiltinRuntimeActions.ShowLayer(runtime, null, request.Layer);
            var layer = runtime.SceneInstances.GetOrAdd(request.Layer.HandleId, id => new Layer { Id = id });
            layer.IsVideo = true;
            layer.VideoPlaybackId = request.PlaybackId;
        }
        TryComplete();
    }

    private async Task ObserveAsync()
    {
        try
        {
            if (presenter is not null)
                await presenter.PlayVideoAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            GameLog.Logger.Warning(error, "Video asset {AssetId} failed on layer {HandleId}", request.Layer.AssetId, request.Layer.HandleId);
        }
        finally
        {
            if (!_catchUpDetached && runtime.SceneInstances.TryGet<Layer>(request.Layer.HandleId, out var layer) &&
                layer.VideoPlaybackId == request.PlaybackId)
                runtime.SceneInstances.Remove<Layer>(request.Layer.HandleId, out _);
            TryComplete();
        }
    }
}
