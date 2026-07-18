namespace GalNet.Control.Runtime.Presentation;

/// <summary>
/// 特效注册表 —— 按名称管理所有 IEffect 实现。
/// </summary>
public sealed class EffectRegistry
{
    private readonly Dictionary<string, Core.View.IEffect> _effects
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>所有活跃特效实例（以 id 索引）</summary>
    private readonly Dictionary<string, ActiveEffect> _active
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>注册特效</summary>
    public void Register(Core.View.IEffect effect)
    {
        _effects[effect.Name] = effect;
    }

    /// <summary>按名称获取特效定义</summary>
    public Core.View.IEffect? Get(string name)
    {
        _effects.TryGetValue(name, out var e);
        return e;
    }

    /// <summary>启动特效并返回其 id</summary>
    public string Start(string effectType, Core.View.IGameView view,
        IReadOnlyDictionary<string, object> parameters)
    {
        var effect = Get(effectType);
        if (effect == null)
            return string.Empty;

        var id = $"{effectType}_{Guid.NewGuid():N}";
        effect.Start(view, parameters);
        _active[id] = new ActiveEffect(effect, view);
        return id;
    }

    /// <summary>按 id 停止特效</summary>
    public void Stop(string effectId)
    {
        if (_active.TryGetValue(effectId, out var active))
        {
            active.Effect.Stop(active.View);
            _active.Remove(effectId);
        }
    }

    /// <summary>停止所有活跃特效</summary>
    public void StopAll()
    {
        foreach (var (id, active) in _active)
        {
            active.Effect.Stop(active.View);
        }
        _active.Clear();
    }

    private sealed record ActiveEffect(Core.View.IEffect Effect, Core.View.IGameView View);
}
