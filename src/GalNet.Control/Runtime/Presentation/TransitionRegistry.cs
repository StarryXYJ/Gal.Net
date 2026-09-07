namespace GalNet.Control.Runtime.Presentation;

/// <summary>
/// 转场注册表 —— 按名称管理所有 Avalonia 转场实现。
/// </summary>
public sealed class TransitionRegistry
{
    private readonly Dictionary<string, IVisualTransition> _transitions
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>注册转场</summary>
    public void Register(IVisualTransition transition)
    {
        _transitions[transition.Name] = transition;
    }

    /// <summary>按名称获取转场</summary>
    public IVisualTransition? Get(string name)
    {
        _transitions.TryGetValue(name, out var t);
        return t;
    }

    /// <summary>是否已注册指定名称的转场</summary>
    public bool Has(string name) => _transitions.ContainsKey(name);
}
