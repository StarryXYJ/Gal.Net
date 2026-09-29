using GalNet.Core.Variable;

namespace GalNet.Runtime.Variables;

public sealed class VariableStore
{
    private readonly Dictionary<string, Variable> _playerVariables = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Variable> _saveVariables = new(StringComparer.Ordinal);
    private readonly Func<string, VariableScope> _scopeResolver;
    private readonly Action<VariableScope, string, Variable>? _onVariableChanged;

    public VariableStore(
        Func<string, VariableScope>? scopeResolver = null,
        Action<VariableScope, string, Variable>? onVariableChanged = null)
    {
        _scopeResolver = scopeResolver ?? (_ => VariableScope.Save);
        _onVariableChanged = onVariableChanged;
    }

    public IReadOnlyDictionary<string, Variable> PlayerSnapshot => _playerVariables;
    public IReadOnlyDictionary<string, Variable> SaveSnapshot => _saveVariables;

    public void Set(string name, object value)
    {
        var normalized = NormalizeName(name);
        var scope = ResolveScope(name);
        var variables = scope == VariableScope.Player ? _playerVariables : _saveVariables;
        if (variables.TryGetValue(normalized, out var current) && HasSameValue(current, value))
            return;

        var variable = GetOrCreate(normalized, scope);
        variable.Name = normalized;
        variable.SetValue(value);
        _onVariableChanged?.Invoke(scope, normalized, CloneVariable(variable, normalized));
    }

    public bool GetBool(string name, bool def = false) =>
        TryGet(name, out var variable) ? variable.AsBool() : def;

    public int GetInt(string name, int def = 0) =>
        TryGet(name, out var variable) ? variable.AsInt() : def;

    public float GetFloat(string name, float def = 0f) =>
        TryGet(name, out var variable) ? variable.AsFloat() : def;

    public string GetString(string name, string def = "") =>
        TryGet(name, out var variable) ? variable.AsString() : def;

    public bool TryGet(string name, out Variable variable)
    {
        var normalized = NormalizeName(name);
        return _playerVariables.TryGetValue(normalized, out variable!)
            || _saveVariables.TryGetValue(normalized, out variable!);
    }

    public IReadOnlyDictionary<string, Variable> GetSnapshot(VariableScope scope) =>
        scope == VariableScope.Player ? _playerVariables : _saveVariables;

    public void RestorePlayerFrom(IReadOnlyDictionary<string, Variable> snapshot)
    {
        _playerVariables.Clear();
        foreach (var (name, variable) in snapshot)
            _playerVariables[NormalizeName(name)] = CloneVariable(variable, NormalizeName(name));
    }

    public void RestoreSaveFrom(IReadOnlyDictionary<string, Variable> snapshot)
    {
        _saveVariables.Clear();
        foreach (var (name, variable) in snapshot)
            _saveVariables[NormalizeName(name)] = CloneVariable(variable, NormalizeName(name));
    }

    private Variable GetOrCreate(string name, VariableScope scope)
    {
        var variables = scope == VariableScope.Player ? _playerVariables : _saveVariables;
        if (!variables.TryGetValue(name, out var variable))
        {
            variable = new Variable { Name = name };
            variables[name] = variable;
        }

        return variable;
    }

    private VariableScope ResolveScope(string name)
    {
        if (name.StartsWith("player.", StringComparison.Ordinal))
            return VariableScope.Player;

        if (name.StartsWith("save.", StringComparison.Ordinal))
            return VariableScope.Save;

        return _scopeResolver(NormalizeName(name));
    }

    private static string NormalizeName(string name)
    {
        if (name.StartsWith("player.", StringComparison.Ordinal))
            return name["player.".Length..];

        if (name.StartsWith("save.", StringComparison.Ordinal))
            return name["save.".Length..];

        return name;
    }

    private static Variable CloneVariable(Variable variable, string name)
    {
        var clone = new Variable { Name = name };
        switch (variable.Type)
        {
            case VariableType.Bool:
                clone.SetValue(variable.AsBool());
                break;
            case VariableType.Int:
                clone.SetValue(variable.AsInt());
                break;
            case VariableType.Float:
                clone.SetValue(variable.AsFloat());
                break;
            default:
                clone.SetValue(variable.AsString());
                break;
        }

        return clone;
    }

    private static bool HasSameValue(Variable variable, object value)
    {
        var candidate = VariableValue.FromObject(value);
        if (variable.Type != candidate.Type)
            return false;

        return variable.Type switch
        {
            VariableType.Bool => variable.AsBool() == candidate.AsBool(),
            VariableType.Int => variable.AsInt() == candidate.AsInt(),
            VariableType.Float => variable.AsFloat().Equals(candidate.AsFloat()),
            _ => string.Equals(variable.AsString(), candidate.AsString(), StringComparison.Ordinal)
        };
    }
}
