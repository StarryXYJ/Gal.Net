using GalNet.Core.Primitives;
using GalNet.Core.Runtime;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

/// <summary>Owns persistent presentation continuations and drains them before replay or disposal.</summary>
public sealed class PersistentSceneReplaySession(
    IEffectPresenter? effects = null,
    IParticlePresenter? particles = null,
    IAnimationPresenter? animations = null,
    ILayerPresenter? layers = null,
    IVideoPresenter? videos = null) : IAsyncDisposable
{
    private readonly List<PrimitiveInstance> _continuations = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _disposeSync = new();
    private volatile bool _disposed;
    private Task? _disposeTask;

    public async Task RestoreAsync(IGameRuntime runtime, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await SuspendCoreAsync(cancellationToken).ConfigureAwait(false);
            await BuiltinPresentationReplay.ReplayPersistentSceneObjectsAsync(runtime,
                effects, particles, animations, layers, videos, _continuations.Add, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task SuspendAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await SuspendCoreAsync(cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    private async Task SuspendCoreAsync(CancellationToken cancellationToken)
    {
        foreach (var instance in _continuations) instance.ApplyPlaybackMode(PrimitiveInvocationMode.CatchUp);
        // Cancellation stops waiting, not ownership: the next replay/disposal must still drain these instances.
        await Task.WhenAll(_continuations.Select(instance => instance.PresentationCompletion))
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        _continuations.Clear();
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeSync)
        {
            _disposed = true;
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { await SuspendCoreAsync(CancellationToken.None).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }
}
