using GalNet.Core.Variable;
using GalVariable = GalNet.Core.Variable.Variable;

namespace GalNet.Core.Services;

/// <summary>
/// Provides variable state to the game runtime and receives change notifications.
/// Editor implements this to bridge editor state with the game engine.
/// A pure game could implement this to read from save files.
/// </summary>
public interface IVariableService
{
    /// <summary>Get current variable snapshot for a given scope.</summary>
    IReadOnlyDictionary<string, GalVariable> GetSnapshot(VariableScope scope);

    /// <summary>Resolve which scope a variable name belongs to.</summary>
    VariableScope ResolveScope(string name);

    /// <summary>Called by the game runtime when a variable value changes.</summary>
    void NotifyVariableChanged(VariableScope scope, string name, GalVariable variable);

    /// <summary>Fired when a variable change needs to be propagated to subscribers.</summary>
    event Action<VariableScope, string, GalVariable>? VariableChanged;
}