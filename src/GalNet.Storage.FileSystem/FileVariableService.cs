using GalNet.Core.Services;
using GalNet.Core.Variable;
using GalVariable = GalNet.Core.Variable.Variable;

namespace GalNet.Storage.FileSystem;

/// <summary>
/// Platform-neutral runtime variable bridge backed by an <see cref="IPlayerVariableStore"/>.
/// Variables prefixed with <c>player.</c> persist globally; all other variables remain in the active save.
/// </summary>
public sealed class FileVariableService : IVariableService
{
    private readonly IPlayerVariableStore _playerStore;
    private readonly Dictionary<string, GalVariable> _playerVariables;
    private readonly Dictionary<string, GalVariable> _saveVariables = new(StringComparer.Ordinal);

    private FileVariableService(IPlayerVariableStore playerStore, IReadOnlyDictionary<string, GalVariable> playerVariables)
    {
        _playerStore = playerStore;
        _playerVariables = playerVariables.ToDictionary(pair => pair.Key, pair => Clone(pair.Key, pair.Value), StringComparer.Ordinal);
    }

    public event Action<VariableScope, string, GalVariable>? VariableChanged;

    public static async Task<FileVariableService> CreateAsync(IPlayerVariableStore playerStore, CancellationToken ct = default) =>
        new(playerStore, await playerStore.LoadAsync(ct));

    public IReadOnlyDictionary<string, GalVariable> GetSnapshot(VariableScope scope) =>
        scope == VariableScope.Player ? _playerVariables : _saveVariables;

    public VariableScope ResolveScope(string name) =>
        name.StartsWith("player.", StringComparison.Ordinal) ? VariableScope.Player : VariableScope.Save;

    public void NotifyVariableChanged(VariableScope scope, string name, GalVariable variable)
    {
        var target = scope == VariableScope.Player ? _playerVariables : _saveVariables;
        target[name] = Clone(name, variable);
        VariableChanged?.Invoke(scope, name, variable);
    }

    public Task FlushPlayerVariablesAsync(CancellationToken ct = default) => _playerStore.SaveAsync(_playerVariables, ct);

    private static GalVariable Clone(string name, GalVariable source)
    {
        var clone = new GalVariable { Name = name };
        clone.SetValue(source.Type switch
        {
            VariableType.Bool => source.AsBool(),
            VariableType.Int => source.AsInt(),
            VariableType.Float => source.AsFloat(),
            _ => source.AsString()
        });
        return clone;
    }
}
