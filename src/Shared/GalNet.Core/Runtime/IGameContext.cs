using GalNet.Core.Scene;
using GalNet.Core.Services;
using GalNet.Core.Variable;
using GalVariable = GalNet.Core.Variable.Variable;

namespace GalNet.Core.Runtime;

/// <summary>Gameplay capabilities borrowed by an instruction or presentation, scoped to one run.</summary>
public interface IGameContext
{
    string CurrentNodeId { get; }
    int EntryIndex { get; }
    bool IsGameEnded { get; }
    ITextResolver TextResolver { get; }
    GameState State { get; }
    ISceneInstanceManager SceneInstances { get; }
    void EndGame();
    void SetVariable(string name, object value);
    GalVariable? GetVariable(string name);
    bool TryGetVariable(string name, out GalVariable variable);
    IReadOnlyDictionary<string, GalVariable> GetVariables(VariableScope scope);
    bool EvaluateCondition(string expression);
    object? EvaluateExpression(string expression);
}
