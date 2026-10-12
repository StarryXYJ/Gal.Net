using GalNet.Core.Primitives;
using GalNet.Core.Runtime;
using GalNet.Presentation.Abstractions.View;
using GalNet.Primitives.Builtins;

namespace GalNet.Sample.Avalonia.Services;

internal sealed class PersistentSceneRestorer(
    IEffectPresenter? effects,
    IParticlePresenter particles,
    IAnimationPresenter animations,
    ILayerPresenter layers,
    IVideoPresenter? videos = null)
{
    private readonly List<PrimitiveInstance> _continuations = [];

    public async Task RestoreAsync(IGameRuntime runtime, CancellationToken cancellationToken)
    {
        await SuspendAsync(cancellationToken);
        await BuiltinPresentationReplay.ReplayPersistentSceneObjectsAsync(
            runtime,
            effects,
            particles,
            animations,
            layers,
            videos,
            _continuations.Add,
            cancellationToken);
    }

    // Called under the Engine execution gate. Logical completion alone is not a cleanup barrier.
    public async Task SuspendAsync(CancellationToken cancellationToken)
    {
        foreach (var instance in _continuations) instance.ApplyPlaybackMode(PrimitiveInvocationMode.CatchUp);
        await Task.WhenAll(_continuations.Select(instance => instance.PresentationCompletion)).WaitAsync(cancellationToken);
        _continuations.Clear();
    }
}
