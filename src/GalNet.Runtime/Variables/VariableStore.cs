using GalNet.Core.Variable;

namespace GalNet.Runtime.Variables;

/// <summary>
/// 运行时变量存储。player 和 save 共用命名空间，不可重名。
/// </summary>
public sealed class VariableStore
{
    private readonly Dictionary<VariableRoute, Variable> _variables = new();

    /// <summary>获取所有变量快照（用于存档）。</summary>
    public IReadOnlyDictionary<VariableRoute, Variable> Snapshot => _variables;

    /// <summary>获取或创建设置变量值。</summary>
    public void Set(VariableRoute route, object value)
    {
        var v = GetOrCreate(route);
        v.SetValue(value);
    }

    /// <summary>获取 bool 值，不存在返回默认。</summary>
    public bool GetBool(VariableRoute route, bool def = false)
    {
        return _variables.TryGetValue(route, out var v) ? v.AsBool() : def;
    }

    /// <summary>获取 int 值。</summary>
    public int GetInt(VariableRoute route, int def = 0)
    {
        return _variables.TryGetValue(route, out var v) ? v.AsInt() : def;
    }

    /// <summary>获取 float 值。</summary>
    public float GetFloat(VariableRoute route, float def = 0f)
    {
        return _variables.TryGetValue(route, out var v) ? v.AsFloat() : def;
    }

    /// <summary>获取 string 值。</summary>
    public string GetString(VariableRoute route, string def = "")
    {
        return _variables.TryGetValue(route, out var v) ? v.AsString() : def;
    }

    /// <summary>尝试获取变量。</summary>
    public bool TryGet(VariableRoute route, out Variable variable)
    {
        return _variables.TryGetValue(route, out variable!);
    }

    /// <summary>从快照恢复（读档用）。</summary>
    public void RestoreFrom(IReadOnlyDictionary<VariableRoute, Variable> snapshot)
    {
        _variables.Clear();
        foreach (var (route, variable) in snapshot)
        {
            _variables[route] = variable;
        }
    }

    private Variable GetOrCreate(VariableRoute route)
    {
        if (!_variables.TryGetValue(route, out var v))
        {
            v = new Variable { Name = route.Path };
            _variables[route] = v;
        }
        return v;
    }
}
