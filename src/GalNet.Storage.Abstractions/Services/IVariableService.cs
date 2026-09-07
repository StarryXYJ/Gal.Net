using GalNet.Core.Variable;
using GalVariable = GalNet.Core.Variable.Variable;

namespace GalNet.Core.Services;

/// <summary>Bridges runtime variable state to a host-owned store or editor.</summary>
public interface IVariableService
{
    IReadOnlyDictionary<string, GalVariable> GetSnapshot(VariableScope scope);
    VariableScope ResolveScope(string name);
    void NotifyVariableChanged(VariableScope scope, string name, GalVariable variable);
    event Action<VariableScope, string, GalVariable>? VariableChanged;
}
