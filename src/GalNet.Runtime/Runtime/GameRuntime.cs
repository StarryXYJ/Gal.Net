using GalNet.Core.Runtime;
using GalNet.Core.Scene;
using GalNet.Core.Services;
using GalNet.Core.Settings;
using GalNet.Runtime.Variables;
using GalVariable = GalNet.Core.Variable.Variable;
using GalNet.Core.Variable;

namespace GalNet.Runtime.Runtime;

/// <summary>
/// 游戏运行时状态 —— 统一管理游戏的位置、变量、场景状态、调用栈。
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
    public SceneState SceneState { get; } = new();
    public ISceneInstanceManager SceneInstances { get; }

    // ── 内部状态 ──
    private readonly VariableStore _variables;
    private readonly ExpressionEvaluator _evaluator;
    private readonly Stack<(string NodeId, int EntryIndex)> _callStack = new();
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

    // ── 调用栈 ──

    public void PushCallStack(string nodeId) => _callStack.Push((nodeId, 0));

    public (string nodeId, int entryIndex)? PopCallStack()
    {
        if (_callStack.Count == 0) return null;
        var saved = _callStack.Pop();
        return saved;
    }

    // ── 存档快照 ──

    public GameSnapshot CreateSnapshot()
    {
        return new GameSnapshot
        {
            Version = GameSnapshot.CurrentFormatVersion,
            NodeId = CurrentNodeId,
            EntryIndex = EntryIndex,
            Variables = _variables.SaveSnapshot.ToDictionary(pair => pair.Key, pair => CloneVariable(pair.Value)),
            SceneState = CloneSceneState(SceneState)
        };
    }

    public void RestoreFrom(GameSnapshot snapshot)
    {
        if (snapshot.Version != GameSnapshot.CurrentFormatVersion)
            throw new InvalidDataException($"Unsupported save version '{snapshot.Version}'.");

        CurrentNodeId = snapshot.NodeId;
        EntryIndex = snapshot.EntryIndex;

        _variables.RestoreSaveFrom(snapshot.Variables);
        SceneState.Layers.Clear();
        SceneState.Layers.AddRange(snapshot.SceneState.Layers.Select(layer => new Layer
        {
            Id = layer.Id,
            AssetId = layer.AssetId,
            Flipbook = layer.Flipbook?.Clone(),
            Color = layer.Color,
            Transform = layer.Transform?.Clone() ?? new LayerTransform(),
            Z = layer.Z,
            DisplayMode = layer.DisplayMode,
            Visible = layer.Visible,
            Opacity = layer.Opacity,
            EffectInstanceIds = [.. layer.EffectInstanceIds]
        }));
        SceneState.ActiveControlIds.Clear();
        SceneState.ActiveControlIds.AddRange(snapshot.SceneState.ActiveControlIds);
        SceneState.ActiveEffectIds.Clear();
        SceneState.ActiveEffectIds.AddRange(snapshot.SceneState.ActiveEffectIds);
        SceneState.ActiveEffects.Clear();
        SceneState.ActiveEffects.AddRange(snapshot.SceneState.ActiveEffects.Select(effect => new ActiveEffectState
        {
            Id = effect.Id,
            ProgramResource = effect.ProgramResource,
            InstanceId = effect.InstanceId,
            TargetHandleId = effect.TargetHandleId,
            Order = effect.Order,
            Parameters = effect.Parameters,
            AnimationValues = effect.AnimationValues.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
        }));
        SceneState.ActiveParticleEmitters.Clear();
        SceneState.ActiveParticleEmitters.AddRange(snapshot.SceneState.ActiveParticleEmitters.Select(emitter => new ActiveParticleEmitterState
        {
            InstanceId = emitter.InstanceId, Definition = emitter.Definition, Z = emitter.Z,
            AnimationValues = emitter.AnimationValues.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
        }));
        SceneState.ActiveAnimations.Clear();
        SceneState.ActiveAnimations.AddRange(snapshot.SceneState.ActiveAnimations.Select(animation => new ActiveAnimationState
        {
            EntryType = animation.EntryType,
            PlaybackHandleId = animation.PlaybackHandleId,
            Parameters = animation.Parameters.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
        }));
        // Effect is the canonical owner of its target. Rebuild the Layer-side index so
        // older saves and malformed duplicate lists cannot leave a dangling association.
        foreach (var layer in SceneState.Layers) layer.EffectInstanceIds.Clear();
        foreach (var effect in SceneState.ActiveEffects.Where(effect => !string.IsNullOrWhiteSpace(effect.TargetHandleId)))
        {
            var target = SceneState.Layers.FirstOrDefault(layer => layer.Id == effect.TargetHandleId);
            if (target is not null) target.EffectInstanceIds.Add(effect.InstanceId);
        }
        SceneInstances.Rebuild(SceneState.Layers.Cast<ISceneInstance>()
            .Concat(SceneState.ActiveEffects.Select(CreateEffectInstance))
            .Concat(SceneState.ActiveParticleEmitters.Select(CreateParticleEmitterInstance)));
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

    private static GalVariable CloneVariable(GalVariable variable)
    {
        var copy = new GalVariable { Uid = variable.Uid, Name = variable.Name };
        switch (variable.Type)
        {
            case VariableType.Bool: copy.SetValue(variable.AsBool()); break;
            case VariableType.Int: copy.SetValue(variable.AsInt()); break;
            case VariableType.Float: copy.SetValue(variable.AsFloat()); break;
            default: copy.SetValue(variable.AsString()); break;
        }
        return copy;
    }

    private static SceneState CloneSceneState(SceneState source) => new()
    {
        Layers = source.Layers.Select(layer => new Layer
        {
            Id = layer.Id,
            AssetId = layer.AssetId,
            Flipbook = layer.Flipbook?.Clone(),
            Color = layer.Color,
            Transform = layer.Transform.Clone(),
            Z = layer.Z,
            DisplayMode = layer.DisplayMode,
            Visible = layer.Visible,
            Opacity = layer.Opacity,
            EffectInstanceIds = [.. layer.EffectInstanceIds]
        }).ToList(),
        ActiveControlIds = [.. source.ActiveControlIds],
        ActiveEffectIds = [.. source.ActiveEffectIds],
        ActiveEffects = source.ActiveEffects.Select(effect => new ActiveEffectState
        {
            Id = effect.Id,
            ProgramResource = effect.ProgramResource,
            InstanceId = effect.InstanceId,
            TargetHandleId = effect.TargetHandleId,
            Order = effect.Order,
            Parameters = effect.Parameters,
            AnimationValues = effect.AnimationValues.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
        }).ToList(),
        ActiveParticleEmitters = source.ActiveParticleEmitters.Select(emitter => new ActiveParticleEmitterState
        {
            InstanceId = emitter.InstanceId,
            Definition = emitter.Definition,
            Z = emitter.Z,
            AnimationValues = emitter.AnimationValues.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
        }).ToList(),
        ActiveAnimations = source.ActiveAnimations.Select(animation => new ActiveAnimationState
        {
            EntryType = animation.EntryType,
            PlaybackHandleId = animation.PlaybackHandleId,
            Parameters = animation.Parameters.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
        }).ToList()
    };
}
