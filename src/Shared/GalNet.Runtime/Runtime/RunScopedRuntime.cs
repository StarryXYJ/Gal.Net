using GalNet.Core.Runtime;
using GalNet.Core.Scene;
using GalNet.Core.Services;
using GalNet.Core.Settings;
using GalNet.Core.Variable;
using GalVariable = GalNet.Core.Variable.Variable;

namespace GalNet.Runtime.Runtime;

/// <summary>Old callbacks retain their old scene and cannot change a restored run's control flow or variables.</summary>
internal sealed class RunScopedRuntime : IGameRuntime
{
    private readonly IGameRuntime _runtime;
    private readonly CancellationToken _cancellation;

    public RunScopedRuntime(IGameRuntime runtime, CancellationToken cancellation)
    {
        _runtime = runtime;
        _cancellation = cancellation;
        SceneState = runtime.SceneState;
        SceneInstances = runtime.SceneInstances;
    }

    public string CurrentNodeId => _runtime.CurrentNodeId;
    public int EntryIndex => _runtime.EntryIndex;
    public bool IsGameEnded => _runtime.IsGameEnded;
    public ITextResolver TextResolver => _runtime.TextResolver;
    public SettingsContainer Settings => _runtime.Settings;
    public SceneState SceneState { get; }
    public ISceneInstanceManager SceneInstances { get; }
    public void JumpTo(string nodeId, int entryIndex = 0) { Check(); _runtime.JumpTo(nodeId, entryIndex); }
    public void SetEntryIndex(int entryIndex) { Check(); _runtime.SetEntryIndex(entryIndex); }
    public void EndGame() { Check(); _runtime.EndGame(); }
    public void SetVariable(string name, object value) { Check(); _runtime.SetVariable(name, value); }
    public GalVariable? GetVariable(string name) { Check(); return _runtime.GetVariable(name); }
    public bool TryGetVariable(string name, out GalVariable variable) { Check(); return _runtime.TryGetVariable(name, out variable!); }
    public IReadOnlyDictionary<string, GalVariable> GetVariables(VariableScope scope) { Check(); return _runtime.GetVariables(scope); }
    public bool EvaluateCondition(string expression) { Check(); return _runtime.EvaluateCondition(expression); }
    public object? EvaluateExpression(string expression) { Check(); return _runtime.EvaluateExpression(expression); }
    public GameSnapshot CreateSnapshot() { Check(); return _runtime.CreateSnapshot(); }
    public void RestoreFrom(GameSnapshot snapshot) => throw new InvalidOperationException("Only the engine can restore a run.");
    private void Check() => _cancellation.ThrowIfCancellationRequested();
}
