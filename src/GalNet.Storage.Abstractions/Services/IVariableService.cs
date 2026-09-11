using GalNet.Core.Variable;
using GalVariable = GalNet.Core.Variable.Variable;

namespace GalNet.Core.Services;

/// <summary>Bridges runtime variable state to a host-owned store or editor.</summary>
public interface IVariableService
{
    /// <summary>Returns a snapshot of the requested scope for runtime initialization or inspection.</summary>
    IReadOnlyDictionary<string, GalVariable> GetSnapshot(VariableScope scope);
    /// <summary>Determines where a variable name is persisted before runtime changes it.</summary>
    VariableScope ResolveScope(string name);
    /// <summary>Receives a runtime update after the scoped variable store has accepted it.</summary>
    void NotifyVariableChanged(VariableScope scope, string name, GalVariable variable);
    event Action<VariableScope, string, GalVariable>? VariableChanged;
}
