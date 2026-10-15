using GalNet.Contracts.Builtins;
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
    public VideoPrimitiveInstance(IVideoPresenter? presenter, PrimitiveCreateContext context, VideoPlayOptions options)
        : this(context.Runtime, presenter, new VideoRenderRequest(Guid.NewGuid().ToString("N"),
            new LayerRenderRequest(options.HandleId ?? "", options.Asset ?? "",
                Arguments.Json<LayerTransform>(options.Transform) ?? new LayerTransform(), options.Z,
                options.DisplayMode ?? LayerDisplayMode.Uniform, options.Opacity), Arguments.Json<double[]>(options.SkipPoints) ?? []),
            options.Blocking ?? false, context.BatchId, context.ScopeCancellation)
    { }

    private Layer? _layer;
    private bool _catchUpDetached;
    private Task _presentationTask = Task.CompletedTask;
    public override Task PresentationCompletion => _presentationTask;
    public override bool IsBlocking => blocking;
    public override bool IsSkippable => blocking && !IsCompleted;

    protected override void OnDispatch()
    {
        SetLayer();
        _presentationTask = ObserveAsync();
    }

    protected override void OnSkip() => presenter?.AdvanceVideo(request.PlaybackId);

    protected override void OnPlaybackModeChanged(PrimitiveInvocationMode mode)
    {
        if (mode == PrimitiveInvocationMode.Normal || (!blocking && mode != PrimitiveInvocationMode.CatchUp)) return;
        _catchUpDetached = !blocking;
        var owned = false;
        runtime.State.Update(() =>
        {
            owned = runtime.SceneInstances.TryGet<Layer>(request.Layer.HandleId, out var layer) && ReferenceEquals(layer, _layer);
            if (owned && blocking) runtime.SceneInstances.Remove<Layer>(request.Layer.HandleId, out _);
        });
        if (owned) presenter?.StopVideo(request.Layer.HandleId);
        TryComplete();
    }

    protected override void OnCatchUpDispatch()
    {
        if (!blocking) SetLayer();
        TryComplete();
    }

    private void SetLayer() => runtime.State.Update(() =>
    {
        BuiltinRuntimeActions.ShowLayer(runtime, null, request.Layer);
        _layer = runtime.State.Table<Layer>().Get(request.Layer.HandleId);
        _layer.IsVideo = true;
    });

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
            runtime.State.Update(() =>
            {
                if (!_catchUpDetached && runtime.SceneInstances.TryGet<Layer>(request.Layer.HandleId, out var layer) &&
                    ReferenceEquals(layer, _layer))
                    runtime.SceneInstances.Remove<Layer>(request.Layer.HandleId, out _);
            });
            TryComplete();
        }
    }
}
