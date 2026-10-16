using GalNet.Core.Graph;
using GalNet.Core.Runtime;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;
using GalNet.Runtime.Engine;
using GalNet.Runtime.Progress;

namespace GalNet.GameHost;

/// <summary>Default engine lifecycle. The caller owns the view, restore session and platform backends.</summary>
public sealed class GameRun : IGameRun
{
    private readonly StateRestoreSession? _restoration;
    private bool _prepared;

    public GameRun(Graph graph, IGameRuntime runtime, IGameView view,
        IGameProgressService? progress = null, IChoicePresenter? choices = null,
        ICatchUpPresentation? catchUp = null, StateRestoreSession? restoration = null)
    {
        Engine = new GameEngine(graph, runtime, view, progress, choices, catchUp);
        _restoration = restoration;
    }

    public GameEngine Engine { get; }
    public GameSnapshot CreateSaveData() => Engine.CreateSaveData();

    public async Task PrepareAsync(GameSnapshot? snapshot, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (snapshot is not null)
        {
            Engine.RestoreFrom(snapshot);
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

    public ValueTask DisposeAsync() => Engine.DisposeAsync();
}
