using GalNet.Core.Runtime;
using GalNet.Core.Scene;
using GalNet.Core.Services;
using GalNet.Core.Settings;
using GalNet.Core.Variable;
using GalNet.Runtime.Variables;
using GalVariable = GalNet.Core.Variable.Variable;

namespace GalNet.Runtime.Runtime;

/// <summary>
/// 游戏运行时状态 —— 统一管理游戏的位置、变量和场景状态。
/// Primitive module 通过 PrimitiveCreateContext.Runtime 访问此实例。
/// </summary>
public sealed class GameRuntime : IGameRuntime
{
    // ── 位置 ──
    public string CurrentNodeId { get; set; } = "";
    public int EntryIndex { get; set; }

    // ── 游戏结束标志 ──
    public bool IsGameEnded { get; set; }

    // ── 核心引用 ──
    public ITextResolver TextResolver { get; }

    // ── 设置 ──
    public SettingsContainer Settings { get; }

    // ── 场景状态 ──
    public SceneState SceneState { get; private set; } = new();
    public ISceneInstanceManager SceneInstances { get; private set; }

    // ── 内部状态 ──
    private readonly VariableStore _variables;
    private readonly ExpressionEvaluator _evaluator;
    private readonly IVariableService? _variableService;

    public GameRuntime(ITextResolver? textResolver, string rootNodeId = "",
        SettingsContainer? settings = null,
        IVariableService? variableService = null)
    {
        TextResolver = textResolver ?? PassthroughTextResolver.Instance;
        CurrentNodeId = rootNodeId;
        Settings = settings ?? new SettingsContainer();
        SceneInstances = new SceneInstanceManager(SceneState);
        _variableService = variableService;

        _variables = new VariableStore(
            variableService is not null ? name => variableService.ResolveScope(name) : null,
            variableService is not null ? OnVariableChanged : null);

        if (variableService is not null)
        {
            _variables.RestorePlayerFrom(variableService.GetSnapshot(VariableScope.Player));
            _variables.RestoreSaveFrom(variableService.GetSnapshot(VariableScope.Save));
        }

        _evaluator = new ExpressionEvaluator(_variables);
    }

    private void OnVariableChanged(VariableScope scope, string name, GalVariable variable)
    {
        _variableService?.NotifyVariableChanged(scope, name, variable);
    }

    public void JumpTo(string nodeId, int entryIndex = 0)
    {
        CurrentNodeId = nodeId;
        EntryIndex = entryIndex;
    }

    public void SetEntryIndex(int entryIndex)
    {
        EntryIndex = entryIndex;
    }

    public void EndGame()
    {
        IsGameEnded = true;
    }

    // ── 变量操作 ──

    public void SetVariable(string name, object value) => _variables.Set(name, value);

    public GalVariable? GetVariable(string name)
    {
        _variables.TryGet(name, out var v);
        return v;
    }

    public bool TryGetVariable(string name, out GalVariable variable) =>
        _variables.TryGet(name, out variable!);

    public IReadOnlyDictionary<string, GalVariable> GetVariables(VariableScope scope) =>
        _variables.GetSnapshot(scope);

    public bool EvaluateCondition(string expression) => _evaluator.EvaluateCondition(expression);

    public object? EvaluateExpression(string expression) => _evaluator.Evaluate(expression);

    // ── 存档快照 ──

    public GameSnapshot CreateSnapshot()
    {
        return new GameSnapshot
        {
            Version = GameSnapshot.CurrentFormatVersion,
            NodeId = CurrentNodeId,
            EntryIndex = EntryIndex,
            Variables = _variables.SaveSnapshot.ToDictionary(pair => pair.Key, pair => SnapshotCopy.CloneVariable(pair.Value)),
            SceneState = SnapshotCopy.CloneSceneState(SceneState)
        };
    }

    public void RestoreFrom(GameSnapshot snapshot)
    {
        if (snapshot.Version != GameSnapshot.CurrentFormatVersion)
            throw new InvalidDataException($"Unsupported save version '{snapshot.Version}'.");

        CurrentNodeId = snapshot.NodeId;
        EntryIndex = snapshot.EntryIndex;

        _variables.RestoreSaveFrom(snapshot.Variables);
        IsGameEnded = false;
        SceneState = SnapshotCopy.CloneSceneState(snapshot.SceneState);
        SceneInstances = new SceneInstanceManager(SceneState);
        SceneInstances.Rebuild(SceneState.Layers.Cast<ISceneInstance>()
            .Concat(SceneState.ActiveEffects.Select(CreateEffectInstance))
            .Concat(SceneState.ActiveParticleEmitters.Select(CreateParticleEmitterInstance))
            .Concat(SceneState.ActiveAnimations.Select(CreateAnimationPlaybackInstance)));
    }

    private static EffectInstance CreateEffectInstance(ActiveEffectState effect)
    {
        var instance = new EffectInstance
        {
            Id = effect.InstanceId,
            EffectId = effect.Id,
            ProgramResource = effect.ProgramResource,
            TargetHandleId = effect.TargetHandleId,
            Order = effect.Order,
            Parameters = effect.Parameters
        };
        instance.RestoreAnimationValues(effect.AnimationValues);
        return instance;
    }

    private static ParticleEmitterInstance CreateParticleEmitterInstance(ActiveParticleEmitterState emitter)
    {
        var instance = new ParticleEmitterInstance(emitter.InstanceId, emitter.Definition, emitter.Z);
        instance.RestoreAnimationValues(emitter.AnimationValues);
        return instance;
    }

    private static AnimationPlaybackInstance CreateAnimationPlaybackInstance(ActiveAnimationState animation) => new()
    {
        Id = animation.PlaybackHandleId,
        LoopMode = animation.LoopMode
    };

}
