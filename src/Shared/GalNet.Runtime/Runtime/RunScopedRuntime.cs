using GalNet.Core.Runtime;
using GalNet.Core.Scene;
using GalNet.Core.Services;
using GalNet.Core.Variable;
using GalVariable = GalNet.Core.Variable.Variable;

namespace GalNet.Runtime.Runtime;

/// <summary>Old callbacks retain their old scene and cannot change a restored run's control flow or variables.</summary>
internal sealed class RunScopedRuntime : IGameContext
{
    private readonly IGameRuntime _runtime;
    private readonly CancellationToken _cancellation;
    private readonly bool _ignoreVariableWrites;

    public RunScopedRuntime(IGameRuntime runtime, CancellationToken cancellation, bool ignoreVariableWrites = false)
    {
        _runtime = runtime;
        _cancellation = cancellation;
        _ignoreVariableWrites = ignoreVariableWrites;
        State = runtime.State;
        SceneInstances = runtime.SceneInstances;
    }

    public string CurrentNodeId => _runtime.CurrentNodeId;
    public int EntryIndex => _runtime.EntryIndex;
    public bool IsGameEnded => _runtime.IsGameEnded;
    public ITextResolver TextResolver => _runtime.TextResolver;
    public GameState State { get; }
    public ISceneInstanceManager SceneInstances { get; }
    public void EndGame() => State.Update(() => { Check(); _runtime.EndGame(); });
    public void SetVariable(string name, object value) => State.Update(() =>
    { Check(); if (!_ignoreVariableWrites) _runtime.SetVariable(name, value); });
    public GalVariable? GetVariable(string name) => Read(() => _runtime.GetVariable(name));
    public bool TryGetVariable(string name, out GalVariable variable) { variable = GetVariable(name)!; return variable is not null; }
    public IReadOnlyDictionary<string, GalVariable> GetVariables(VariableScope scope) => Read(() => _runtime.GetVariables(scope));
    public bool EvaluateCondition(string expression) => Read(() => _runtime.EvaluateCondition(expression));
    public object? EvaluateExpression(string expression) => Read(() => _runtime.EvaluateExpression(expression));
    private T Read<T>(Func<T> read) => State.Read(() => { Check(); return read(); });
    private void Check() => _cancellation.ThrowIfCancellationRequested();
}
