using GalNet.Core.Graph;
using GalNet.Core.Runtime;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;
using GalNet.Runtime.Engine;
using GalNet.Runtime.Progress;

namespace GalNet.GameHost;

/// <summary>Default engine lifecycle. The caller owns the view and platform backends; a supplied restore session is borrowed.</summary>
public sealed class GameRun : IGameRun
{
    private readonly StateRestoreSession? _restoration;
    private bool _prepared;
    private readonly bool _ownsRestoration;
    private readonly object _disposeGate = new();
    private Task? _disposeTask;

    public GameRun(Graph graph, IGameRuntime runtime, IPrimitiveDispatcher view,
        IGameProgressService? progress = null, IChoicePresenter? choices = null,
        ICatchUpPresentation? catchUp = null, StateRestoreSession? restoration = null)
    {
        Engine = new GameEngine(graph, runtime, view, progress, choices, catchUp);
        _ownsRestoration = restoration is null && view is CompositeGameView;
        _restoration = restoration ?? (view is CompositeGameView composite ? new StateRestoreSession(composite) : null);
    }

    public GameEngine Engine { get; }
    public GameSnapshot CreateSaveData() => Engine.CreateSaveData();
    public Task ValidateSnapshotAsync(GameSnapshot snapshot, CancellationToken cancellationToken) =>
        Engine.ValidateRestoreAsync(snapshot, cancellationToken);

    public async Task PrepareAsync(GameSnapshot? snapshot, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (snapshot is not null)
        {
            await Engine.RestoreFromAsync(snapshot, cancellationToken);
            if (_restoration is not null)
                await Engine.SynchronizePresentationAsync(_restoration.RestoreAsync, cancellationToken);
        }
        _prepared = true;
    }

    public async Task StartPreparedAsync(CancellationToken cancellationToken = default)
    {
        if (!_prepared) throw new InvalidOperationException("No prepared game is available to start.");
        _prepared = false;
        await Engine.AdvanceAsync(cancellationToken);
    }

    public async Task StopAsync()
    {
        await Engine.StopAsync();
        if (_restoration is not null) await _restoration.SuspendAsync(CancellationToken.None);
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate) return new(_disposeTask ??= DisposeCoreAsync());
    }
    private async Task DisposeCoreAsync()
    {
        var cleanup = new ResourceCleanup();
        await cleanup.RunAsync(Engine.DisposeAsync);
        if (_ownsRestoration && _restoration is not null) await cleanup.RunAsync(_restoration.DisposeAsync);
        cleanup.ThrowIfFailed();
    }
}
