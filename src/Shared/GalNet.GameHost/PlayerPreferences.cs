using GalNet.Core.Variable;
using GalNet.Runtime.Variables;

namespace GalNet.GameHost;

/// <summary>Typed player preference validation and persistence, independent of settings controls.</summary>
public sealed class PlayerPreferences(IVariableService variables)
{
    public const string LocaleVariable = "player.galnet.settings.locale";
    public const string TextSpeedVariable = "player.galnet.settings.textSpeed";
    public double Number(string name, double fallback, double maximum)
    {
        var stored = variables.GetSnapshot(VariableScope.Player);
        var value = stored.TryGetValue(name, out var saved) && saved.Type == VariableType.Float ? saved.AsFloat() : fallback;
        return double.IsFinite(value) && value >= 0 && value <= maximum ? value : fallback;
    }
    public int Integer(string name, int fallback, int maximum)
    {
        var stored = variables.GetSnapshot(VariableScope.Player);
        var value = stored.TryGetValue(name, out var saved) && saved.Type == VariableType.Int ? saved.AsInt() : fallback;
        return value >= 0 && value <= maximum ? value : fallback;
    }
    public bool Boolean(string name, bool fallback)
    {
        var stored = variables.GetSnapshot(VariableScope.Player);
        return stored.TryGetValue(name, out var saved) && saved.Type == VariableType.Bool ? saved.AsBool() : fallback;
    }
    public void Set(string name, object value)
    {
        var variable = new Variable { Name = name };
        variable.SetValue(value);
        variables.NotifyVariableChanged(VariableScope.Player, name, variable);
    }
}
