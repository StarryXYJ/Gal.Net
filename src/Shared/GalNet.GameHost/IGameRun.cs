using GalNet.Core.Runtime;

namespace GalNet.GameHost;

/// <summary>One replaceable run. Implementations own the engine and their presentation bindings.</summary>
public interface IGameRun : IAsyncDisposable
{
    Task ValidateSnapshotAsync(GameSnapshot snapshot, CancellationToken cancellationToken);
    Task PrepareAsync(GameSnapshot? snapshot, CancellationToken cancellationToken);
    Task StartPreparedAsync(CancellationToken cancellationToken);
    Task StopAsync();
    GameSnapshot CreateSaveData();
}

/// <summary>Optional startup initialization, executed after preparation and before the first entry.</summary>
public interface IParameterizedGameRun
{
    Task InitializeSaveVariablesAsync(IReadOnlyDictionary<string, object> variables, CancellationToken cancellationToken);
}
