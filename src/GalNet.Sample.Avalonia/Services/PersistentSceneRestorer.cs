using GalNet.Core.Runtime;
using GalNet.Presentation.Abstractions.View;
using GalNet.Primitives.Builtins;

namespace GalNet.Sample.Avalonia.Services;

internal sealed class PersistentSceneRestorer(
    IEffectPresenter? effects,
    IParticlePresenter particles,
    IAnimationPresenter animations,
    ILayerPresenter layers)
{
    public Task RestoreAsync(IGameRuntime runtime, CancellationToken cancellationToken) =>
        BuiltinPresentationReplay.ReplayPersistentSceneObjectsAsync(
            runtime,
            effects,
            particles,
            animations,
            layers,
            cancellationToken);
}
