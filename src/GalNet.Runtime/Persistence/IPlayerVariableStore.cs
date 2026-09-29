using GalNet.Core.Variable;
using GalVariable = GalNet.Core.Variable.Variable;

namespace GalNet.Runtime.Persistence;

/// <summary>
/// Persists player-scope variables independently from individual save slots.
/// </summary>
public interface IPlayerVariableStore
{
    Task<IReadOnlyDictionary<string, GalVariable>> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(IReadOnlyDictionary<string, GalVariable> variables, CancellationToken ct = default);
}
